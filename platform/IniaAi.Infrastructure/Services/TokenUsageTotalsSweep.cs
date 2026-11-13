using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Infrastructure.Persistence;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Adds the token usage ledger rows not yet in the totals to <see cref="TokenUsageDailyTotal"/> and
/// <see cref="TokenUsageExecutionTotal"/>, and marks them added in the same transaction, so a row is
/// counted once whether a read finds it in the totals or among the rows still unmarked. One host
/// sweeps at a time; another host finds the lock held and skips its round.
/// </summary>
public sealed class TokenUsageTotalsSweep
{
    // SQL Server locks a whole table once one statement holds 5,000 locks on it, which would stall
    // every model call recording its usage; a batch stays well under that.
    internal const int BATCH_SIZE = 1000;

    private readonly UserDataDbContext _db;

    public TokenUsageTotalsSweep(UserDataDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// How many ledger rows this round added. Zero when there was nothing to add or another host
    /// holds the sweep.
    /// </summary>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        if (!await _db.TokenUsages.AnyAsync(t => !t.IsInTotals, cancellationToken))
        {
            return 0;
        }

        using DatabaseSessionLock? sweepLock = DatabaseSessionLock.TryAcquire(_db, DatabaseLockName.TokenUsageTotals, wait: false);
        if (sweepLock is null)
        {
            return 0;
        }

        int added = 0;
        int batch;
        do
        {
            batch = await AddBatchAsync(cancellationToken);
            added += batch;
        }
        while (batch == BATCH_SIZE);

        if (added >= BATCH_SIZE && _db.Database.IsSqlServer())
        {
            _ = await _db.Database.ExecuteSqlRawAsync(TokenUsageNotInTotalsIndex.SQL_SERVER_REFRESH_STATISTICS, cancellationToken);  // Raw SQL reviewed: constant statistics-refresh statement; no request data
        }

        return added;
    }

    private async Task<int> AddBatchAsync(CancellationToken cancellationToken)
    {
        IExecutionStrategy strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            List<LedgerRow> rows = await _db.TokenUsages
                .AsNoTracking()
                .Where(t => !t.IsInTotals)
                .OrderBy(t => t.OrganizationId)
                .ThenBy(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Take(BATCH_SIZE)
                .Select(t => new LedgerRow(
                    t.Id,
                    t.OrganizationId,
                    t.CreatedAt,
                    t.UsageType,
                    t.WorkflowTypeId,
                    t.UserId,
                    t.WorkflowExecutionId,
                    t.Components
                        .Select(c => new TokenUsageComponent { ComponentType = c.ComponentType, Units = c.Units, Cost = c.Cost })
                        .ToList()))
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
            {
                return 0;
            }

            await AddToDailyTotalsAsync(rows, cancellationToken);
            await AddToExecutionTotalsAsync(rows, cancellationToken);
            _ = await _db.SaveChangesAsync(cancellationToken);

            // The lock keeps a second sweeper out; this keeps a row from being counted twice if the
            // lock's session dies mid-round and another host takes it: that host's update waits on
            // these row locks, then finds the rows marked and rolls its whole batch back.
            List<string> ids = [.. rows.Select(r => r.Id)];
            int marked = await _db.TokenUsages
                .Where(t => ids.Contains(t.Id) && !t.IsInTotals)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsInTotals, true), cancellationToken);
            if (marked != rows.Count)
            {
                throw new InvalidOperationException(
                    $"Another usage-totals sweep added {rows.Count - marked} of these {rows.Count} ledger rows first; this batch is rolled back.");
            }

            await transaction.CommitAsync(cancellationToken);

            return rows.Count;
        });
    }

    private async Task AddToDailyTotalsAsync(List<LedgerRow> rows, CancellationToken cancellationToken)
    {
        List<string> organizationIds = [.. rows.Select(r => r.OrganizationId).Distinct(StringComparer.Ordinal)];
        List<DateOnly> days = [.. rows.Select(r => r.Day).Distinct()];
        Dictionary<DailyKey, TokenUsageDailyTotal> totals = (await _db.TokenUsageDailyTotals
                .Where(d => organizationIds.Contains(d.OrganizationId) && days.Contains(d.Day))
                .ToListAsync(cancellationToken))
            .GroupBy(DailyKey.Of)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (IGrouping<DailyKey, LedgerRow> group in rows.GroupBy(DailyKey.Of))
        {
            if (!totals.TryGetValue(group.Key, out TokenUsageDailyTotal? total))
            {
                total = new TokenUsageDailyTotal
                {
                    OrganizationId = group.Key.OrganizationId,
                    Day = group.Key.Day,
                    UsageType = group.Key.UsageType,
                    WorkflowTypeId = group.Key.WorkflowTypeId,
                    UserId = group.Key.UserId,
                };
                _ = _db.TokenUsageDailyTotals.Add(total);
            }

            foreach (LedgerRow row in group)
            {
                row.AddTo(total);
            }
        }
    }

    private async Task AddToExecutionTotalsAsync(List<LedgerRow> rows, CancellationToken cancellationToken)
    {
        List<IGrouping<string, LedgerRow>> groups = [.. rows
            .Where(r => r.WorkflowExecutionId is not null)
            .GroupBy(r => r.WorkflowExecutionId!, StringComparer.OrdinalIgnoreCase)];
        if (groups.Count == 0)
        {
            return;
        }

        List<string> executionIds = [.. groups.Select(g => g.Key)];
        Dictionary<string, TokenUsageExecutionTotal> totals = await _db.TokenUsageExecutionTotals
            .Where(e => executionIds.Contains(e.WorkflowExecutionId))
            .ToDictionaryAsync(e => e.WorkflowExecutionId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (IGrouping<string, LedgerRow> group in groups)
        {
            DateTime firstUsageAt = group.Min(r => r.CreatedAt);
            if (!totals.TryGetValue(group.Key, out TokenUsageExecutionTotal? total))
            {
                total = new TokenUsageExecutionTotal
                {
                    WorkflowExecutionId = group.Key,
                    OrganizationId = group.First().OrganizationId,
                    FirstUsageAt = firstUsageAt,
                };
                _ = _db.TokenUsageExecutionTotals.Add(total);
            }
            else if (firstUsageAt < total.FirstUsageAt)
            {
                total.FirstUsageAt = firstUsageAt;
            }

            total.WorkflowTypeId ??= group.Select(r => r.WorkflowTypeId).FirstOrDefault(id => id is not null);
            foreach (LedgerRow row in group)
            {
                row.AddTo(total);
            }
        }
    }

    private sealed record LedgerRow(
        string Id,
        string OrganizationId,
        DateTime CreatedAt,
        UsageType UsageType,
        string? WorkflowTypeId,
        string? UserId,
        string? WorkflowExecutionId,
        List<TokenUsageComponent> Components)
    {
        public DateOnly Day => DateOnly.FromDateTime(CreatedAt);

        public void AddTo<T>(TokenUsageTotal<T> total)
            where T : class
        {
            total.OperationCount++;
            foreach (TokenUsageComponent component in Components)
            {
                total.Add(component);
            }
        }
    }

    private sealed record DailyKey(string OrganizationId, DateOnly Day, UsageType UsageType, string? WorkflowTypeId, string? UserId)
    {
        public static DailyKey Of(LedgerRow row)
        {
            return new DailyKey(row.OrganizationId, row.Day, row.UsageType, row.WorkflowTypeId, row.UserId);
        }

        public static DailyKey Of(TokenUsageDailyTotal total)
        {
            return new DailyKey(total.OrganizationId, total.Day, total.UsageType, total.WorkflowTypeId, total.UserId);
        }
    }
}
