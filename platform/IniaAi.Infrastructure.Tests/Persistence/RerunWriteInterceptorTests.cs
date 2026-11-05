using IniaAi.Domain.Business.Reporting;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Reruns;
using IniaAi.Infrastructure.Persistence;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SqliteUserDataDbContext = IniaAi.Testing.DependencyInjection.SqliteUserDataDbContext;
using SqliteUserDataSchema = IniaAi.Testing.DependencyInjection.SqliteUserDataSchema;

namespace IniaAi.Infrastructure.Tests.Persistence;

/// <summary>
/// A rerun saves its own execution but no change to live data: the save step drops every added,
/// changed or deleted row of a <see cref="LiveDataAttribute"/> entity while the rerun scope is open,
/// whichever handler or block wrote it.
/// </summary>
public sealed class RerunWriteInterceptorTests : IDisposable
{
    private const string STORED_FINANCIALS = "financials before the rerun";
    private const string RERUN_FINANCIALS = "financials the rerun extracted";

    private readonly SqliteConnection _connection = SqliteUserDataSchema.OpenInMemoryDatabase();
    private readonly string _organizationId;

    public RerunWriteInterceptorTests()
    {
        using SqliteUserDataDbContext seed = Context();
        var organization = new Organization("Rerun organization");
        _ = seed.Organizations.Add(organization);
        _ = seed.SaveChanges();
        _organizationId = organization.Id;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task InARerun_ObservationsAndCounterpartyChangesAreNotSaved_TheExecutionIs()
    {
        string counterpartyId = await SeedCounterpartyAsync();
        string executionId;

        using (RerunWrites.Suppress())
        {
            executionId = await WriteARunAsync(counterpartyId);
        }

        await using SqliteUserDataDbContext read = Context();
        Assert.Equal(0, await read.Set<PrivateEquityMonitoringObservation>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await read.Set<HedgeFundsMonitoringObservation>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(STORED_FINANCIALS, (await read.Counterparties.SingleAsync(c => c.Id == counterpartyId, TestContext.Current.CancellationToken)).LastFinancialAnalysisResult);
        Assert.Equal(1, await read.Counterparties.CountAsync(TestContext.Current.CancellationToken));
        Assert.True(await read.WorkflowExecutions.AnyAsync(e => e.Id == executionId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InAReport_TheSameWritesAreSaved()
    {
        string counterpartyId = await SeedCounterpartyAsync();

        _ = await WriteARunAsync(counterpartyId);

        await using SqliteUserDataDbContext read = Context();
        Assert.Equal(1, await read.Set<PrivateEquityMonitoringObservation>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await read.Set<HedgeFundsMonitoringObservation>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(RERUN_FINANCIALS, (await read.Counterparties.SingleAsync(c => c.Id == counterpartyId, TestContext.Current.CancellationToken)).LastFinancialAnalysisResult);
        Assert.Equal(2, await read.Counterparties.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheRerunScope_ReachesWorkOnAnotherThread_AndEndsWhenDisposed()
    {
        string counterpartyId = await SeedCounterpartyAsync();

        using (RerunWrites.Suppress())
        {
            await Task.Run(() => WriteARunAsync(counterpartyId), TestContext.Current.CancellationToken);
        }

        Assert.False(RerunWrites.AreSuppressed);

        await using SqliteUserDataDbContext read = Context();
        Assert.Equal(0, await read.Set<PrivateEquityMonitoringObservation>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InARerun_ADeletedLiveRowStays()
    {
        string counterpartyId = await SeedCounterpartyAsync();

        using (RerunWrites.Suppress())
        {
            await using SqliteUserDataDbContext context = Context();
            _ = context.Counterparties.Remove(await context.Counterparties.SingleAsync(c => c.Id == counterpartyId, TestContext.Current.CancellationToken));
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using SqliteUserDataDbContext read = Context();
        Assert.True(await read.Counterparties.AnyAsync(c => c.Id == counterpartyId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InARerun_AnObservationWithAllocations_IsDroppedWhole_AndTheSaveSucceeds()
    {
        string executionId;

        using (RerunWrites.Suppress())
        {
            await using SqliteUserDataDbContext context = Context();
            var execution = new WorkflowExecution { OrganizationId = _organizationId };
            _ = context.WorkflowExecutions.Add(execution);
            var observation = new PrivateEquityMonitoringObservation { OrganizationId = _organizationId, FundName = "Fund A" };
            observation.Allocations.Add(new PrivateEquityMonitoringAllocation { Name = "Holding 1", Observation = observation });
            _ = context.Add(observation);
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            executionId = execution.Id;
        }

        await using SqliteUserDataDbContext read = Context();
        Assert.Equal(0, await read.Set<PrivateEquityMonitoringObservation>().CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await read.Set<PrivateEquityMonitoringAllocation>().CountAsync(TestContext.Current.CancellationToken));
        Assert.True(await read.WorkflowExecutions.AnyAsync(e => e.Id == executionId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InARerun_ANewRowThatPointsAtANewLiveRow_IsDropped_OneThatPointsAtAnExistingRowIsSaved()
    {
        string counterpartyId = await SeedCounterpartyAsync();
        string onNewCounterparty;
        string onExistingCounterparty;

        using (RerunWrites.Suppress())
        {
            await using SqliteUserDataDbContext context = Context();
            var created = new Counterparty(_organizationId, "Created by a rerun");
            var first = new WorkflowExecution { OrganizationId = _organizationId, Counterparty = created };
            var second = new WorkflowExecution { OrganizationId = _organizationId, CounterpartyId = counterpartyId };
            context.WorkflowExecutions.AddRange(first, second);
            _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            onNewCounterparty = first.Id;
            onExistingCounterparty = second.Id;
        }

        await using SqliteUserDataDbContext read = Context();
        Assert.False(await read.WorkflowExecutions.AnyAsync(e => e.Id == onNewCounterparty, TestContext.Current.CancellationToken));
        Assert.True(await read.WorkflowExecutions.AnyAsync(e => e.Id == onExistingCounterparty, TestContext.Current.CancellationToken));
        Assert.Equal(1, await read.Counterparties.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AReportScopeInsideARerunScope_SavesLiveData()
    {
        string counterpartyId = await SeedCounterpartyAsync();

        using (RerunWrites.Suppress())
        {
            using (RerunWrites.Scope(isRerun: false))
            {
                _ = await WriteARunAsync(counterpartyId);
            }

            Assert.True(RerunWrites.AreSuppressed);
        }

        await using SqliteUserDataDbContext read = Context();
        Assert.Equal(1, await read.Set<PrivateEquityMonitoringObservation>().CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>What a monitoring run and a counterparty write-back save, through one fresh context.</summary>
    private async Task<string> WriteARunAsync(string counterpartyId)
    {
        await using SqliteUserDataDbContext context = Context();

        var execution = new WorkflowExecution { OrganizationId = _organizationId };
        _ = context.WorkflowExecutions.Add(execution);
        _ = context.Add(new PrivateEquityMonitoringObservation { OrganizationId = _organizationId, FundName = "Fund A" });
        _ = context.Add(new HedgeFundsMonitoringObservation { OrganizationId = _organizationId, FundName = "Fund B" });
        _ = context.Counterparties.Add(new Counterparty(_organizationId, "Created by a run"));

        Counterparty counterparty = await context.Counterparties.SingleAsync(c => c.Id == counterpartyId, TestContext.Current.CancellationToken);
        counterparty.LastFinancialAnalysisResult = RERUN_FINANCIALS;

        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return execution.Id;
    }

    private async Task<string> SeedCounterpartyAsync()
    {
        await using SqliteUserDataDbContext context = Context();
        var counterparty = new Counterparty(_organizationId, "Corp") { LastFinancialAnalysisResult = STORED_FINANCIALS };
        _ = context.Counterparties.Add(counterparty);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return counterparty.Id;
    }

    private SqliteUserDataDbContext Context()
    {
        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new RerunWriteInterceptor(NullLogger<RerunWriteInterceptor>.Instance))
            .Options;

        return new SqliteUserDataDbContext(options);
    }
}
