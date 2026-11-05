using System.Text.Json;
using IniaAi.Application.Queries.TokenUsage;
using IniaAi.Application.Queries.TokenUsage.GetMyUsageSummary;
using IniaAi.Application.Queries.TokenUsage.GetOrganizationUsageDashboard;
using IniaAi.Application.Queries.TokenUsage.GetWorkflowTypeUsageSummary;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

// The usage reads sum the daily and per-execution totals plus the ledger rows the sweep has not
// added yet, in one statement: a set operation over a grouped ledger projection, a date taken from
// a timestamp, and a distinct count over the union, which a provider may translate differently.
// This holds every read against sums taken straight from the seeded rows, before any sweep, after
// one, with new rows waiting beside the totals, and after a second sweep.
public abstract class TokenUsageTotalsOnBothProvidersTests
{
    private const string TEST_DB_PREFIX = "iniaai_usagetotals_";
    private const string GUARD_NAME = "the usage totals guard";
    private const string ORGANIZATION_ID = "org-usage-totals";
    private const string OTHER_ORGANIZATION_ID = "org-usage-totals-other";
    private const string USER_ID = "user-usage-totals";
    private const string OTHER_USER_ID = "user-usage-totals-other";
    private const string WORKFLOW_TYPE_ID = "UsageTotalsType";
    private const string OTHER_WORKFLOW_TYPE_ID = "UsageTotalsOtherType";
    private const string KEPT_EXECUTION_ID = "execution-usage-totals-kept";
    private const string OTHER_EXECUTION_ID = "execution-usage-totals-other";
    private const string PURGED_EXECUTION_ID = "execution-usage-totals-purged";

    private readonly ICoreDatabaseFixture _core;
    private readonly string _provider;
    private readonly string _skipReason;

    protected TokenUsageTotalsOnBothProvidersTests(ICoreDatabaseFixture core, string provider, string skipReason)
    {
        _core = core;
        _provider = provider;
        _skipReason = skipReason;
    }

    [Fact]
    public async Task The_usage_reads_count_every_ledger_row_once_whether_it_is_in_the_totals_or_not()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        if (!_core.DatabaseAvailable)
        {
            Assert.Skip(_skipReason);
        }

        DbContextOptions<UserDataDbContext> userDataOptions = await BothProvidersUserDataDatabase.OptionsOrSkipAsync(
            _provider, TEST_DB_PREFIX, GUARD_NAME, cancellationToken);

        try
        {
            DateTime today = DateTime.UtcNow.Date;
            List<TokenUsage> ledger = [.. FirstLedger(today)];

            await using (UserDataDbContext setup = new(userDataOptions))
            {
                await setup.Database.MigrateAsync(cancellationToken);
                await SeedUserDataAsync(setup, ledger, cancellationToken);
            }

            await using (CoreDbContext core = _core.CreateContext())
            {
                await SeedCoreAsync(core, cancellationToken);
            }

            await using ServiceProvider services = BothProvidersHandlers.BuildServices(userDataOptions, _core);

            Reads beforeAnySweep = await ReadAsync(services, cancellationToken);
            AssertMatchesLedger(beforeAnySweep, ledger, today);

            Assert.Equal(ledger.Count, await SweepAsync(userDataOptions, cancellationToken));
            await AssertEveryRowIsInTotalsAsync(userDataOptions, cancellationToken);
            Reads afterSweep = await ReadAsync(services, cancellationToken);
            Assert.Equal(Fingerprint(beforeAnySweep), Fingerprint(afterSweep));

            List<TokenUsage> laterRows = [.. LaterLedger(today)];
            await using (UserDataDbContext add = new(userDataOptions))
            {
                add.TokenUsages.AddRange(laterRows);
                _ = await add.SaveChangesAsync(cancellationToken);
            }

            ledger.AddRange(laterRows);
            Reads withRowsWaiting = await ReadAsync(services, cancellationToken);
            AssertMatchesLedger(withRowsWaiting, ledger, today);

            Assert.Equal(laterRows.Count, await SweepAsync(userDataOptions, cancellationToken));
            Assert.Equal(0, await SweepAsync(userDataOptions, cancellationToken));
            Reads afterSecondSweep = await ReadAsync(services, cancellationToken);
            Assert.Equal(Fingerprint(withRowsWaiting), Fingerprint(afterSecondSweep));

            await PurgeExecutionAsync(userDataOptions, PURGED_EXECUTION_ID, cancellationToken);
            WorkflowTypeUsageSummaryDto afterPurge = await WorkflowTypeSummaryAsync(services, cancellationToken);
            Assert.Equal(3, afterPurge.ExecutionCount);
            Assert.Equal(
                [KEPT_EXECUTION_ID, OTHER_EXECUTION_ID],
                afterPurge.RecentExecutions.Select(e => e.WorkflowExecutionId).Order(StringComparer.Ordinal));
        }
        finally
        {
            await using UserDataDbContext cleanup = new(userDataOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    // A backlog of a full batch or more is what a deployment's first sweep meets; on SQL Server the
    // sweep then refreshes the statistics of the index that finds the rows not yet in the totals.
    [Fact]
    public async Task A_backlog_of_more_than_one_batch_is_swept_in_one_round()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<UserDataDbContext> userDataOptions = await BothProvidersUserDataDatabase.OptionsOrSkipAsync(
            _provider, TEST_DB_PREFIX, GUARD_NAME, cancellationToken);

        try
        {
            DateTime today = DateTime.UtcNow.Date;
            List<TokenUsage> backlog = [.. Enumerable.Range(0, 1_001).Select(i =>
                Usage(ORGANIZATION_ID, USER_ID, WORKFLOW_TYPE_ID, KEPT_EXECUTION_ID, UsageType.LlmBlockExecution, today.AddMinutes(i % 600), i))];

            await using (UserDataDbContext setup = new(userDataOptions))
            {
                await setup.Database.MigrateAsync(cancellationToken);
                await SeedUserDataAsync(setup, backlog, cancellationToken);
            }

            Assert.Equal(backlog.Count, await SweepAsync(userDataOptions, cancellationToken));
            await AssertEveryRowIsInTotalsAsync(userDataOptions, cancellationToken);

            await using UserDataDbContext read = new(userDataOptions);
            IReadOnlyDictionary<string, UsageExecutionSlice> spend = await SpendAsync(read, [KEPT_EXECUTION_ID], cancellationToken);
            Assert.Equal(Cost(backlog), spend[KEPT_EXECUTION_ID].Cost);
        }
        finally
        {
            await using UserDataDbContext cleanup = new(userDataOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    // The lock is what keeps two hosts from sweeping the same rows: a host that finds it held skips
    // its round, and the rows wait for the next one.
    [Fact]
    public async Task A_host_that_finds_the_sweep_lock_held_skips_its_round()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DbContextOptions<UserDataDbContext> userDataOptions = await BothProvidersUserDataDatabase.OptionsOrSkipAsync(
            _provider, TEST_DB_PREFIX, GUARD_NAME, cancellationToken);

        try
        {
            List<TokenUsage> ledger = [.. FirstLedger(DateTime.UtcNow.Date)];
            await using (UserDataDbContext setup = new(userDataOptions))
            {
                await setup.Database.MigrateAsync(cancellationToken);
                await SeedUserDataAsync(setup, ledger, cancellationToken);
            }

            await using (UserDataDbContext otherHost = new(userDataOptions))
            {
                using DatabaseSessionLock? held = DatabaseSessionLock.TryAcquire(otherHost, DatabaseLockName.TokenUsageTotals, wait: false);
                Assert.NotNull(held);
                Assert.Equal(0, await SweepAsync(userDataOptions, cancellationToken));
            }

            Assert.Equal(ledger.Count, await SweepAsync(userDataOptions, cancellationToken));
        }
        finally
        {
            await using UserDataDbContext cleanup = new(userDataOptions);
            _ = await cleanup.Database.EnsureDeletedAsync(cancellationToken);
        }
    }

    private static void AssertMatchesLedger(Reads reads, List<TokenUsage> ledger, DateTime today)
    {
        List<TokenUsage> ours = [.. ledger.Where(t => t.OrganizationId == ORGANIZATION_ID)];
        DateTime weekStart = today.AddDays(-(int)today.DayOfWeek);

        AssertPeriod(reads.Dashboard.AllTime, ours);
        AssertPeriod(reads.Dashboard.Today, [.. ours.Where(t => t.CreatedAt >= today)]);
        AssertPeriod(reads.Dashboard.ThisWeek, [.. ours.Where(t => t.CreatedAt >= weekStart)]);
        AssertPeriod(reads.MyUsage.AllTime, [.. ours.Where(t => t.UserId == USER_ID)]);

        foreach (UsageByTypeDto byType in reads.Dashboard.ByUsageType)
        {
            Assert.Equal(
                Tokens(ours.Where(t => t.UsageType.ToString() == byType.UsageType)),
                byType.TotalTokens);
        }

        UsageByWorkflowTypeDto workflowType = Assert.Single(reads.Dashboard.ByWorkflowType, w => w.WorkflowTypeId == WORKFLOW_TYPE_ID);
        List<TokenUsage> ofWorkflowType = [.. ours.Where(t => t.WorkflowTypeId == WORKFLOW_TYPE_ID)];
        int executionCount = ofWorkflowType.Select(t => t.WorkflowExecutionId).OfType<string>().Distinct().Count();
        Assert.Equal(Tokens(ofWorkflowType), workflowType.TotalTokens);
        Assert.Equal(Cost(ofWorkflowType), workflowType.TotalCost);
        Assert.Equal(executionCount, workflowType.ExecutionCount);
        Assert.Equal("Usage Totals Type", workflowType.WorkflowTypeName);

        UsageByUserDto user = Assert.Single(reads.Dashboard.ByUser, u => u.UserId == USER_ID);
        Assert.Equal(ours.Count(t => t.UserId == USER_ID), user.OperationCount);

        DailyUsageTrendDto todayTrend = Assert.Single(reads.Dashboard.DailyTrend, d => d.Date == DateOnly.FromDateTime(today));
        List<TokenUsage> ofToday = [.. ours.Where(t => t.CreatedAt >= today)];
        Assert.Equal(Tokens(ofToday.Where(t => (int)t.UsageType < 20)), todayTrend.LlmTokens);
        Assert.Equal(Units(ofToday.Where(t => t.UsageType == UsageType.EmbeddingChunk), PricingComponentType.Embedding), todayTrend.EmbeddingTokens);
        Assert.DoesNotContain(reads.Dashboard.DailyTrend, d => d.Date < DateOnly.FromDateTime(today.AddDays(-30)));

        Assert.Equal(Tokens(ofWorkflowType), reads.WorkflowType.TotalTokens);
        Assert.Equal(Units(ofWorkflowType.Where(t => t.UsageType == UsageType.EmbeddingChunk), PricingComponentType.Embedding), reads.WorkflowType.TotalEmbeddingTokens);
        Assert.Equal(executionCount, reads.WorkflowType.ExecutionCount);
        string latestExecutionId = ofWorkflowType
            .Where(t => t.WorkflowExecutionId is not null)
            .GroupBy(t => t.WorkflowExecutionId!)
            .MaxBy(g => g.Min(t => t.CreatedAt))!
            .Key;
        Assert.Equal(latestExecutionId, reads.WorkflowType.RecentExecutions[0].WorkflowExecutionId);

        List<TokenUsage> keptRows = [.. ours.Where(t => t.WorkflowExecutionId == KEPT_EXECUTION_ID)];
        UsageExecutionSlice kept = reads.Spend[KEPT_EXECUTION_ID];
        Assert.Equal(Cost(keptRows), kept.Cost);
        Assert.Equal(Units(keptRows, PricingComponentType.Input), kept.InputTokens);
        Assert.Equal(Units(keptRows, PricingComponentType.Output), kept.OutputTokens);
        Assert.False(reads.Spend.ContainsKey("execution-never-ran"));
    }

    private static void AssertPeriod(TokenUsageSummaryDto period, List<TokenUsage> rows)
    {
        Assert.Equal(Units(rows, PricingComponentType.Input), period.TotalInputTokens);
        Assert.Equal(Units(rows, PricingComponentType.Output), period.TotalOutputTokens);
        Assert.Equal(Units(rows, PricingComponentType.CacheWrite5m) + Units(rows, PricingComponentType.CacheWrite1h), period.TotalCacheCreationTokens);
        Assert.Equal(Units(rows, PricingComponentType.CacheRead), period.TotalCacheReadTokens);
        Assert.Equal(Cost(rows), period.TotalCost);
        Assert.Equal(rows.Count, period.OperationCount);
    }

    private static long Units(IEnumerable<TokenUsage> rows, PricingComponentType componentType)
    {
        return rows.SelectMany(t => t.Components).Where(c => c.ComponentType == componentType).Sum(c => (long)c.Units);
    }

    private static long Tokens(IEnumerable<TokenUsage> rows)
    {
        List<TokenUsage> list = [.. rows];
        return Units(list, PricingComponentType.Input) + Units(list, PricingComponentType.Output);
    }

    private static decimal Cost(IEnumerable<TokenUsage> rows)
    {
        return rows.SelectMany(t => t.Components).Sum(c => c.Cost);
    }

    private static async Task<Reads> ReadAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        OrganizationUsageDashboardDto dashboard = await BothProvidersHandlers.HandleAsync<GetOrganizationUsageDashboardQueryHandler, OrganizationUsageDashboardDto>(
            services, handler => handler.Handle(new GetOrganizationUsageDashboardQuery(ORGANIZATION_ID, USER_ID), cancellationToken));
        MyUsageSummaryDto myUsage = await BothProvidersHandlers.HandleAsync<GetMyUsageSummaryQueryHandler, MyUsageSummaryDto>(
            services, handler => handler.Handle(new GetMyUsageSummaryQuery(ORGANIZATION_ID, USER_ID), cancellationToken));
        WorkflowTypeUsageSummaryDto workflowType = await WorkflowTypeSummaryAsync(services, cancellationToken);

        using IServiceScope scope = services.CreateScope();
        UserDataDbContext userData = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        IReadOnlyDictionary<string, UsageExecutionSlice> spend = await SpendAsync(
            userData, [KEPT_EXECUTION_ID, OTHER_EXECUTION_ID, "execution-never-ran"], cancellationToken);

        return new Reads(dashboard, myUsage, workflowType, spend);
    }

    // What the validation estimate reads as an execution's spend: its totals plus the rows not yet in them.
    private static async Task<IReadOnlyDictionary<string, UsageExecutionSlice>> SpendAsync(
        UserDataDbContext userData,
        List<string> executionIds,
        CancellationToken cancellationToken)
    {
        return await TokenUsageSlices.ByExecution(userData, ORGANIZATION_ID)
            .Where(e => executionIds.Contains(e.WorkflowExecutionId))
            .PerExecution()
            .ToDictionaryAsync(e => e.WorkflowExecutionId, cancellationToken);
    }

    private static Task<WorkflowTypeUsageSummaryDto> WorkflowTypeSummaryAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        return BothProvidersHandlers.HandleAsync<GetWorkflowTypeUsageSummaryQueryHandler, WorkflowTypeUsageSummaryDto>(
            services, handler => handler.Handle(new GetWorkflowTypeUsageSummaryQuery(ORGANIZATION_ID, USER_ID, WORKFLOW_TYPE_ID), cancellationToken));
    }

    // The reads as text, without the moment each was taken.
    private static string Fingerprint(Reads reads)
    {
        foreach (TokenUsageSummaryDto period in new[]
        {
            reads.Dashboard.Today, reads.Dashboard.ThisWeek, reads.Dashboard.ThisMonth, reads.Dashboard.AllTime,
            reads.MyUsage.Today, reads.MyUsage.ThisWeek, reads.MyUsage.ThisMonth, reads.MyUsage.AllTime,
        })
        {
            period.PeriodEnd = default;
        }

        return JsonSerializer.Serialize(new
        {
            reads.Dashboard,
            reads.MyUsage,
            reads.WorkflowType,
            Spend = reads.Spend.OrderBy(s => s.Key, StringComparer.Ordinal).ToList(),
        });
    }

    private static async Task<int> SweepAsync(DbContextOptions<UserDataDbContext> options, CancellationToken cancellationToken)
    {
        await using UserDataDbContext context = new(options);
        return await new TokenUsageTotalsSweep(context).SweepOnceAsync(cancellationToken);
    }

    private static async Task AssertEveryRowIsInTotalsAsync(DbContextOptions<UserDataDbContext> options, CancellationToken cancellationToken)
    {
        await using UserDataDbContext context = new(options);
        Assert.False(await context.TokenUsages.AnyAsync(t => !t.IsInTotals, cancellationToken));
    }

    private static async Task PurgeExecutionAsync(DbContextOptions<UserDataDbContext> options, string executionId, CancellationToken cancellationToken)
    {
        await using UserDataDbContext context = new(options);
        _ = await CleanupServiceTestHarness.CreateService(context).PurgeExecutionTreesAsync(ORGANIZATION_ID, [executionId], cancellationToken);
    }

    // Rows over three days, two workflow types and none, two users and none, three usage types and
    // every component type a total keeps apart, plus one other organization's row.
    private static IEnumerable<TokenUsage> FirstLedger(DateTime today)
    {
        string?[] users = [USER_ID, OTHER_USER_ID, null];
        string?[] workflowTypes = [WORKFLOW_TYPE_ID, OTHER_WORKFLOW_TYPE_ID, null];
        UsageType[] usageTypes = [UsageType.LlmBlockExecution, UsageType.EmbeddingChunk, UsageType.LlmOcrTranscription];
        int[] daysAgo = [0, 3, 40];

        for (int i = 0; i < 36; i++)
        {
            string? workflowTypeId = workflowTypes[i % 3];
            yield return Usage(
                ORGANIZATION_ID,
                users[i / 3 % 3],
                workflowTypeId,
                workflowTypeId == WORKFLOW_TYPE_ID ? (i % 2 == 0 ? KEPT_EXECUTION_ID : PURGED_EXECUTION_ID) : null,
                usageTypes[i / 9 % 3],
                today.AddDays(-daysAgo[i / 12 % 3]).AddHours(1).AddMinutes(i),
                i);
        }

        yield return Usage(OTHER_ORGANIZATION_ID, null, WORKFLOW_TYPE_ID, null, UsageType.LlmBlockExecution, today.AddHours(1), 99);
    }

    // New rows for a day and an execution the totals already hold, and for an execution they do not.
    private static IEnumerable<TokenUsage> LaterLedger(DateTime today)
    {
        yield return Usage(ORGANIZATION_ID, USER_ID, WORKFLOW_TYPE_ID, KEPT_EXECUTION_ID, UsageType.LlmBlockExecution, today.AddHours(2), 101);
        yield return Usage(ORGANIZATION_ID, USER_ID, WORKFLOW_TYPE_ID, OTHER_EXECUTION_ID, UsageType.LlmBlockExecution, today.AddHours(3), 102);
        yield return Usage(ORGANIZATION_ID, OTHER_USER_ID, WORKFLOW_TYPE_ID, OTHER_EXECUTION_ID, UsageType.EmbeddingChunk, today.AddHours(3).AddMinutes(1), 103);
    }

    private static TokenUsage Usage(
        string organizationId,
        string? userId,
        string? workflowTypeId,
        string? executionId,
        UsageType usageType,
        DateTime createdAt,
        int seed)
    {
        List<TokenUsageComponent> components = usageType == UsageType.EmbeddingChunk
            ? [Component(PricingComponentType.Embedding, 500 + seed, 0.00001m * (seed + 1))]
            :
            [
                Component(PricingComponentType.Input, 1_000 + (seed * 7), 0.003m * (seed + 1)),
                Component(PricingComponentType.Output, 100 + seed, 0.015m * (seed + 1)),
                Component(seed % 2 == 0 ? PricingComponentType.CacheWrite5m : PricingComponentType.CacheWrite1h, 50 + seed, 0.0001m),
                Component(PricingComponentType.CacheRead, 2_000 + seed, 0.00003m * (seed + 1)),
                Component(PricingComponentType.Reasoning, 10 + seed, 0.0002m),
            ];

        return new TokenUsage
        {
            OrganizationId = organizationId,
            UserId = userId,
            WorkflowTypeId = workflowTypeId,
            WorkflowExecutionId = executionId,
            UsageType = usageType,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            Provider = "aws-bedrock",
            Family = "anthropic",
            ModelId = "claude-usage-totals",
            CreatedAt = createdAt,
            Components = components,
        };
    }

    private static TokenUsageComponent Component(PricingComponentType componentType, int units, decimal cost)
    {
        return new TokenUsageComponent { ComponentType = componentType, Units = units, Cost = cost };
    }

    private static async Task SeedUserDataAsync(UserDataDbContext context, List<TokenUsage> ledger, CancellationToken cancellationToken)
    {
        context.Organizations.AddRange(
            new Organization("Usage Totals Org", ORGANIZATION_ID),
            new Organization("Usage Totals Other Org", OTHER_ORGANIZATION_ID));
        context.Users.AddRange(
            new User(ORGANIZATION_ID) { Id = USER_ID, UserName = "usage@example.com", Email = "usage@example.com" },
            new User(ORGANIZATION_ID) { Id = OTHER_USER_ID, UserName = "other@example.com", Email = "other@example.com" });
        context.WorkflowExecutions.AddRange(
            Execution(KEPT_EXECUTION_ID),
            Execution(OTHER_EXECUTION_ID),
            Execution(PURGED_EXECUTION_ID));
        _ = await context.SaveChangesAsync(cancellationToken);

        context.TokenUsages.AddRange(ledger);
        _ = await context.SaveChangesAsync(cancellationToken);
    }

    private static WorkflowExecution Execution(string id)
    {
        return new WorkflowExecution
        {
            Id = id,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            Title = id,
            UserId = USER_ID,
            OrganizationId = ORGANIZATION_ID,
            WorkflowState = WorkflowState.Draft,
        };
    }

    private static async Task SeedCoreAsync(CoreDbContext context, CancellationToken cancellationToken)
    {
        if (await context.WorkflowTypes.AnyAsync(type => type.Id == WORKFLOW_TYPE_ID, cancellationToken))
        {
            return;
        }

        _ = context.WorkflowTypes.Add(new WorkflowType(WORKFLOW_TYPE_ID, "Usage Totals Type"));
        _ = await context.SaveChangesAsync(cancellationToken);
    }

    private sealed record Reads(
        OrganizationUsageDashboardDto Dashboard,
        MyUsageSummaryDto MyUsage,
        WorkflowTypeUsageSummaryDto WorkflowType,
        IReadOnlyDictionary<string, UsageExecutionSlice> Spend);
}

public sealed class TokenUsageTotalsOnBothProvidersPostgresTests(PostgresCoreDatabaseFixture fixture)
    : TokenUsageTotalsOnBothProvidersTests(fixture, BothProvidersUserDataDatabase.POSTGRESQL, "Set INIA_TEST_PG to run the usage totals guard on PostgreSQL."),
    IClassFixture<PostgresCoreDatabaseFixture>;

public sealed class TokenUsageTotalsOnBothProvidersSqlServerTests(SqlServerCoreDatabaseFixture fixture)
    : TokenUsageTotalsOnBothProvidersTests(fixture, BothProvidersUserDataDatabase.MSSQL, "Set INIA_TEST_MSSQL to run the usage totals guard on SQL Server."),
    IClassFixture<SqlServerCoreDatabaseFixture>;
