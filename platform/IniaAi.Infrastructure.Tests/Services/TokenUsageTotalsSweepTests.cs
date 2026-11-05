using ErrorOr;
using IniaAi.Application.Queries.TokenUsage;
using IniaAi.Application.Queries.TokenUsage.GetOrganizationUsageDashboard;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// The sweep moves ledger rows into the daily and per-execution totals and marks them in the same
/// transaction; a usage read sums the totals plus the rows still unmarked. Whatever the sweep has
/// done, a read must count every row exactly once. The provider-specific translations are held on
/// SQL Server and PostgreSQL by <c>TokenUsageTotalsOnBothProvidersTests</c>; this runs in CI.
/// </summary>
public sealed class TokenUsageTotalsSweepTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-sweep";
    private const string EXECUTION_ID = "execution-sweep";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<UserDataDbContext> _options;
    private readonly SqliteCoreDatabase _core = new();

    public TokenUsageTotalsSweepTests()
    {
        _connection = SqliteUserDataSchema.OpenInMemoryDatabase();
        _options = new DbContextOptionsBuilder<UserDataDbContext>().UseSqlite(_connection).Options;
    }

    public void Dispose()
    {
        _core.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task A_sweep_adds_each_row_once_and_a_read_counts_the_same_before_and_after()
    {
        DateTime today = DateTime.UtcNow.Date;
        await SeedAsync([.. Enumerable.Range(0, 12).Select(i => Usage(today.AddDays(-(i % 3)).AddHours(1), i))]);
        string before = await ReadAsync();

        Assert.Equal(12, await SweepAsync());
        Assert.Equal(0, await SweepAsync());

        await using (SqliteUserDataDbContext context = new(_options))
        {
            Assert.False(await context.TokenUsages.AnyAsync(t => !t.IsInTotals, TestContext.Current.CancellationToken));
            Assert.Equal(12, await context.TokenUsageDailyTotals.SumAsync(d => d.OperationCount, TestContext.Current.CancellationToken));
            Assert.Equal(12, Assert.Single(await context.TokenUsageExecutionTotals.ToListAsync(TestContext.Current.CancellationToken)).OperationCount);
        }

        Assert.Equal(before, await ReadAsync());

        await SeedAsync(Usage(today.AddHours(2), 50));
        string withARowWaiting = await ReadAsync();
        Assert.NotEqual(before, withARowWaiting);

        Assert.Equal(1, await SweepAsync());
        Assert.Equal(withARowWaiting, await ReadAsync());
    }

    [Fact]
    public async Task A_backlog_larger_than_one_batch_is_added_in_one_round()
    {
        DateTime today = DateTime.UtcNow.Date;
        int backlog = TokenUsageTotalsSweep.BATCH_SIZE + 1;
        await SeedAsync([.. Enumerable.Range(0, backlog).Select(i => Usage(today.AddMinutes(i), i))]);

        Assert.Equal(backlog, await SweepAsync());

        await using SqliteUserDataDbContext context = new(_options);
        Assert.Equal(backlog, await context.TokenUsageDailyTotals.SumAsync(d => d.OperationCount, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_execution_starts_at_its_earliest_row_even_when_that_row_is_swept_last()
    {
        DateTime today = DateTime.UtcNow.Date;
        await SeedAsync(Usage(today.AddHours(5), 1));
        Assert.Equal(1, await SweepAsync());
        await SeedAsync(Usage(today.AddHours(1), 2));
        Assert.Equal(1, await SweepAsync());

        await using SqliteUserDataDbContext context = new(_options);
        TokenUsageExecutionTotal total = await context.TokenUsageExecutionTotals.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(today.AddHours(1), total.FirstUsageAt);
        Assert.Equal(2, total.OperationCount);
    }

    private async Task<int> SweepAsync()
    {
        await using SqliteUserDataDbContext context = new(_options);
        return await new TokenUsageTotalsSweep(context).SweepOnceAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedAsync(params TokenUsage[] usages)
    {
        await using SqliteUserDataDbContext context = new(_options);
        if (!await context.Organizations.AnyAsync(TestContext.Current.CancellationToken))
        {
            _ = context.Organizations.Add(new Organization("Sweep Org", ORGANIZATION_ID));
            _ = context.WorkflowExecutions.Add(new WorkflowExecution { Id = EXECUTION_ID, OrganizationId = ORGANIZATION_ID, Title = "Sweep" });
        }

        context.TokenUsages.AddRange(usages);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // The dashboard and the execution's spend as text, without the moment they were read.
    private async Task<string> ReadAsync()
    {
        await using SqliteUserDataDbContext context = new(_options);
        ErrorOr<OrganizationUsageDashboardDto> dashboard = await new GetOrganizationUsageDashboardQueryHandler(context, _core.Context).Handle(
            new GetOrganizationUsageDashboardQuery(ORGANIZATION_ID, "user-sweep"),
            TestContext.Current.CancellationToken);
        Assert.False(dashboard.IsError);
        foreach (TokenUsageSummaryDto period in new[] { dashboard.Value.Today, dashboard.Value.ThisWeek, dashboard.Value.ThisMonth, dashboard.Value.AllTime })
        {
            period.PeriodEnd = default;
        }

        UsageExecutionSlice spend = await TokenUsageSlices.ByExecution(context, ORGANIZATION_ID)
            .Where(e => e.WorkflowExecutionId == EXECUTION_ID)
            .PerExecution()
            .SingleAsync(TestContext.Current.CancellationToken);

        return System.Text.Json.JsonSerializer.Serialize(new { dashboard.Value, Spend = spend });
    }

    private static TokenUsage Usage(DateTime createdAt, int seed)
    {
        return new TokenUsage
        {
            OrganizationId = ORGANIZATION_ID,
            WorkflowTypeId = "wt-sweep",
            WorkflowExecutionId = EXECUTION_ID,
            UsageType = seed % 2 == 0 ? UsageType.LlmBlockExecution : UsageType.EmbeddingChunk,
            ExecutionContext = TokenExecutionContext.WorkflowExecution,
            Provider = "aws-bedrock",
            Family = "anthropic",
            ModelId = "claude-sweep",
            CreatedAt = createdAt,
            Components =
            [
                new TokenUsageComponent { ComponentType = PricingComponentType.Input, Units = 100 + seed, Cost = 0.001m * (seed + 1) },
                new TokenUsageComponent { ComponentType = PricingComponentType.Output, Units = 10 + seed, Cost = 0.002m },
                new TokenUsageComponent { ComponentType = PricingComponentType.Embedding, Units = 5 + seed, Cost = 0.0001m },
            ],
        };
    }
}
