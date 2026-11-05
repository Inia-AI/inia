using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.TokenUsage;

/// <summary>
/// An organization's token usage as rows to sum: the totals the usage-totals sweep keeps, plus every
/// ledger row it has not added yet, read in one statement. Under a snapshot read (PostgreSQL, or SQL
/// Server with read-committed snapshot) each ledger row counts once. Under SQL Server's locking reads,
/// a sweep batch that commits while the statement runs can be counted twice or not at all by that one
/// read.
/// </summary>
public static class TokenUsageSlices
{
    public static IQueryable<UsageDaySlice> ByDay(IUserDataDbContext userData, string organizationId)
    {
        IQueryable<UsageDaySlice> totals = userData.TokenUsageDailyTotals
            .AsNoTracking()
            .Where(d => d.OrganizationId == organizationId)
            .Select(d => new UsageDaySlice
            {
                Day = d.Day,
                UsageType = d.UsageType,
                WorkflowTypeId = d.WorkflowTypeId,
                UserId = d.UserId,
                InputTokens = d.InputTokens,
                OutputTokens = d.OutputTokens,
                CacheWriteTokens = d.CacheWriteTokens,
                CacheReadTokens = d.CacheReadTokens,
                EmbeddingTokens = d.EmbeddingTokens,
                Cost = d.Cost,
                OperationCount = d.OperationCount,
            });

        IQueryable<UsageDaySlice> notYetInTotals = userData.TokenUsages
            .AsNoTracking()
            .Where(t => t.OrganizationId == organizationId && !t.IsInTotals)
            .Select(t => new UsageDaySlice
            {
                Day = DateOnly.FromDateTime(t.CreatedAt),
                UsageType = t.UsageType,
                WorkflowTypeId = t.WorkflowTypeId,
                UserId = t.UserId,
                InputTokens = t.Components.AsQueryable().Where(TokenUsageTotalComponents.IsInput).Sum(c => (long)c.Units),
                OutputTokens = t.Components.AsQueryable().Where(TokenUsageTotalComponents.IsOutput).Sum(c => (long)c.Units),
                CacheWriteTokens = t.Components.AsQueryable().Where(TokenUsageTotalComponents.IsCacheWrite).Sum(c => (long)c.Units),
                CacheReadTokens = t.Components.AsQueryable().Where(TokenUsageTotalComponents.IsCacheRead).Sum(c => (long)c.Units),
                EmbeddingTokens = t.Components.AsQueryable().Where(TokenUsageTotalComponents.IsEmbedding).Sum(c => (long)c.Units),
                Cost = t.Components.Sum(c => c.Cost),
                OperationCount = 1,
            });

        return totals.Concat(notYetInTotals);
    }

    /// <summary>
    /// One row per execution and totals row, or per ledger row not yet in the totals: an execution
    /// can appear more than once, so filter here and sum with <see cref="PerExecution"/>.
    /// </summary>
    public static IQueryable<UsageExecutionSlice> ByExecution(IUserDataDbContext userData, string organizationId)
    {
        IQueryable<UsageExecutionSlice> totals = userData.TokenUsageExecutionTotals
            .AsNoTracking()
            .Where(e => e.OrganizationId == organizationId)
            .Select(e => new UsageExecutionSlice
            {
                WorkflowExecutionId = e.WorkflowExecutionId,
                WorkflowTypeId = e.WorkflowTypeId,
                FirstUsageAt = e.FirstUsageAt,
                InputTokens = e.InputTokens,
                OutputTokens = e.OutputTokens,
                Cost = e.Cost,
            });

        IQueryable<UsageExecutionSlice> notYetInTotals = userData.TokenUsages
            .AsNoTracking()
            .Where(t => t.OrganizationId == organizationId && !t.IsInTotals && t.WorkflowExecutionId != null)
            .Select(t => new UsageExecutionSlice
            {
                WorkflowExecutionId = t.WorkflowExecutionId!,
                WorkflowTypeId = t.WorkflowTypeId,
                FirstUsageAt = t.CreatedAt,
                InputTokens = t.Components.AsQueryable().Where(TokenUsageTotalComponents.IsInput).Sum(c => (long)c.Units),
                OutputTokens = t.Components.AsQueryable().Where(TokenUsageTotalComponents.IsOutput).Sum(c => (long)c.Units),
                Cost = t.Components.Sum(c => c.Cost),
            });

        return totals.Concat(notYetInTotals);
    }

    public static IQueryable<UsageExecutionSlice> PerExecution(this IQueryable<UsageExecutionSlice> slices)
    {
        return slices
            .GroupBy(e => e.WorkflowExecutionId)
            .Select(g => new UsageExecutionSlice
            {
                WorkflowExecutionId = g.Key,
                WorkflowTypeId = g.Max(e => e.WorkflowTypeId),
                FirstUsageAt = g.Min(e => e.FirstUsageAt),
                InputTokens = g.Sum(e => e.InputTokens),
                OutputTokens = g.Sum(e => e.OutputTokens),
                Cost = g.Sum(e => e.Cost),
            });
    }
}

/// <summary>
/// The sums a usage total keeps, named as on <see cref="TokenUsageTotal{T}"/>.
/// </summary>
public abstract class UsageSums
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long CacheWriteTokens { get; init; }

    public long CacheReadTokens { get; init; }

    public long EmbeddingTokens { get; init; }

    public decimal Cost { get; init; }

    public int OperationCount { get; init; }

    public long Tokens => InputTokens + OutputTokens;
}

public sealed class UsageDaySlice : UsageSums
{
    public DateOnly Day { get; init; }

    public UsageType UsageType { get; init; }

    public string? WorkflowTypeId { get; init; }

    public string? UserId { get; init; }
}

public sealed class UsageExecutionSlice
{
    public string WorkflowExecutionId { get; init; } = string.Empty;

    public string? WorkflowTypeId { get; init; }

    public DateTime FirstUsageAt { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public decimal Cost { get; init; }
}
