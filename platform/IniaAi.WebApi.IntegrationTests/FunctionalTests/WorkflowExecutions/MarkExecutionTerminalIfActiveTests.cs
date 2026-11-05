using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.WorkflowExecutions;

/// <summary>
/// Integration tests for <see cref="IWorkflowOutputService"/>'s isolated terminal-state
/// persistence against real Postgres — the relational path runs the atomic conditional
/// UPDATE that the EF in-memory provider cannot exercise (see
/// WorkflowOutputServiceTerminalStateTests for the in-memory guard coverage).
/// No Organization rows are seeded: WorkflowExecutions has no FK to Organizations, and
/// any org row created here would get a system user attached by SystemUserSeeder on the
/// next in-proc app boot, making the org impossible to hard-remove during cleanup.
/// </summary>
public class MarkExecutionTerminalIfActiveTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private readonly ClientBuilder _apiBuilder;
    private readonly List<string> _seededExecutionIds = [];

    public MarkExecutionTerminalIfActiveTests(ApiTestFixture fixture)
    {
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _apiBuilder.CleanExecutionsByIdsAsync(_seededExecutionIds);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_RunningExecution_FlipsToFailedWithTimestampsAsync()
    {
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Running);

        await ResolveServiceAndRunAsync(service =>
            service.MarkExecutionAsFailedIfActiveAsync(executionId, organizationId));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Failed, reloaded.WorkflowState);
        Assert.NotNull(reloaded.StartedAt);
        Assert.NotNull(reloaded.CompletedAt);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_AlreadyCancelled_IsNotOverwrittenAsync()
    {
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Cancelled);

        await ResolveServiceAndRunAsync(service =>
            service.MarkExecutionAsFailedIfActiveAsync(executionId, organizationId));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Cancelled, reloaded.WorkflowState);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_WithAClassifiedFailure_PersistsKindAndTruncatedDetailAsync()
    {
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Running);
        string overlongDetail = new('x', WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH + 500);

        await ResolveServiceAndRunAsync(service =>
            service.MarkExecutionAsFailedIfActiveAsync(
                executionId,
                organizationId,
                WorkflowExecutionFailureKinds.CRITICAL_BLOCK_FAILURE,
                overlongDetail));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Failed, reloaded.WorkflowState);
        Assert.Equal(WorkflowExecutionFailureKinds.CRITICAL_BLOCK_FAILURE, reloaded.FailureKind);
        Assert.Equal(WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH, reloaded.FailureDetail?.Length);
    }

    [Fact]
    public async Task MarkExecutionAsCancelledIfActiveAsync_QueuedExecution_FlipsToCancelledWithoutStartedAtAsync()
    {
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Queued);

        await ResolveServiceAndRunAsync(service =>
            service.MarkExecutionAsCancelledIfActiveAsync(executionId, organizationId));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Cancelled, reloaded.WorkflowState);
        Assert.Null(reloaded.StartedAt);
        Assert.NotNull(reloaded.CompletedAt);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_DetailSplitsASurrogatePair_StillMarksFailedAsync()
    {
        // PostgreSQL rejects a lone surrogate at encode time, so a truncation that cuts inside the
        // pair would abort the very UPDATE that records the failure. The cut must land before it.
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Running);
        string detailStraddlingTheBoundary =
            new string('x', WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH - 1) + "🙂" + new string('x', 500);

        await ResolveServiceAndRunAsync(service =>
            service.MarkExecutionAsFailedIfActiveAsync(
                executionId,
                organizationId,
                WorkflowExecutionFailureKinds.RESULTS_LOST,
                detailStraddlingTheBoundary));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Failed, reloaded.WorkflowState);
        Assert.Equal(WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH - 1, reloaded.FailureDetail?.Length);
    }

    [Fact]
    public async Task TryTransitionStateIfActiveAsync_ActiveExecution_WinsAndMirrorsTheStoredTimestampsAsync()
    {
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Queued);
        WorkflowExecution callerCopy = DetachedCopy(executionId, organizationId, WorkflowState.Queued);

        bool won = await RunInScopeAsync(uow =>
            uow.WorkflowExecutions.TryTransitionStateIfActiveAsync(callerCopy, WorkflowState.Running));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.True(won);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Running, reloaded.WorkflowState);
        Assert.NotNull(reloaded.StartedAt);
        Assert.Equal(reloaded.StartedAt, callerCopy.StartedAt);
        Assert.Null(reloaded.CompletedAt);
    }

    [Fact]
    public async Task TryTransitionStateIfActiveAsync_RowAlreadyStamped_MirrorsTheRowsStartedAt_NotItsOwnAsync()
    {
        // The stored row already carries StartedAt from another writer while the caller's copy is
        // stale. The UPDATE's COALESCE keeps the row's value, and the caller must end up holding
        // that value — not a locally computed one the database never stored.
        DateTime existingStartedAt = MillisecondPrecision(DateTime.UtcNow.AddHours(-1));
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(
            organizationId, WorkflowState.ProcessingDocuments, existingStartedAt);
        WorkflowExecution callerCopy = DetachedCopy(executionId, organizationId, WorkflowState.ProcessingDocuments);

        bool won = await RunInScopeAsync(uow =>
            uow.WorkflowExecutions.TryTransitionStateIfActiveAsync(callerCopy, WorkflowState.Running));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.True(won);
        Assert.NotNull(reloaded);
        Assert.Equal(existingStartedAt, reloaded.StartedAt);
        Assert.Equal(existingStartedAt, callerCopy.StartedAt);
    }

    [Fact]
    public async Task TryTransitionStateIfActiveAsync_WithAWarning_StampsBothColumnsAndMirrorsThemAsync()
    {
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Running);
        WorkflowExecution callerCopy = DetachedCopy(executionId, organizationId, WorkflowState.Running);

        bool won = await RunInScopeAsync(uow =>
            uow.WorkflowExecutions.TryTransitionStateIfActiveAsync(
                callerCopy,
                WorkflowState.Draft,
                warningKind: WorkflowExecutionWarningKinds.UNVERIFIED_RELEASE,
                warningDetail: "Verification 'Verifier' released its work unverified — still failing: Totals recompute"));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.True(won);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Draft, reloaded.WorkflowState);
        Assert.Equal(WorkflowExecutionWarningKinds.UNVERIFIED_RELEASE, reloaded.WarningKind);
        Assert.Contains("Totals recompute", reloaded.WarningDetail);
        Assert.Equal(reloaded.WarningKind, callerCopy.WarningKind);
        Assert.Equal(reloaded.WarningDetail, callerCopy.WarningDetail);
    }

    [Fact]
    public async Task TryTransitionStateIfActiveAsync_WithoutAWarning_LeavesTheColumnsNullAsync()
    {
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Running);
        WorkflowExecution callerCopy = DetachedCopy(executionId, organizationId, WorkflowState.Running);

        bool won = await RunInScopeAsync(uow =>
            uow.WorkflowExecutions.TryTransitionStateIfActiveAsync(callerCopy, WorkflowState.Draft));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.True(won);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded.WarningKind);
        Assert.Null(reloaded.WarningDetail);
    }

    [Fact]
    public async Task TryTransitionStateIfActiveAsync_TerminalExecution_RefusesTheWriteAsync()
    {
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Cancelled);
        WorkflowExecution callerCopy = DetachedCopy(executionId, organizationId, WorkflowState.Running);

        bool won = await RunInScopeAsync(uow =>
            uow.WorkflowExecutions.TryTransitionStateIfActiveAsync(callerCopy, WorkflowState.Draft));

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.False(won);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Cancelled, reloaded.WorkflowState);
    }

    [Fact]
    public async Task UpdateReplayOutcomeAsync_WritesTheJsonColumnAndCoverageCountersAsync()
    {
        const string UNMATCHED_JSON = "[{\"Reason\":\"uncovered\"}]";
        string organizationId = $"TerminalTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, WorkflowState.Running);

        _ = await RunInScopeAsync(async uow =>
        {
            await uow.WorkflowExecutions.UpdateReplayOutcomeAsync(
                executionId,
                organizationId,
                UNMATCHED_JSON,
                freezeReplayedCount: 2,
                freezeEligibleCount: 3);
            return true;
        });

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.NotNull(reloaded);
        Assert.Equal(UNMATCHED_JSON, reloaded.ReplayUnmatchedJson);
        Assert.Equal(2, reloaded.FreezeReplayedCount);
        Assert.Equal(3, reloaded.FreezeEligibleCount);
    }

    private static WorkflowExecution DetachedCopy(string executionId, string organizationId, WorkflowState state)
    {
        return new WorkflowExecution
        {
            Id = executionId,
            OrganizationId = organizationId,
            WorkflowState = state,
        };
    }

    private static DateTime MillisecondPrecision(DateTime value)
    {
        return new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
    }

    private async Task<T> RunInScopeAsync<T>(Func<IUserDataUnitOfWork, Task<T>> action)
    {
        (IServiceScope? scope, IUserDataUnitOfWork? uow) = _apiBuilder.GetNewScope<IUserDataUnitOfWork>();
        using (scope)
        {
            return await action(uow);
        }
    }

    private async Task ResolveServiceAndRunAsync(Func<IWorkflowOutputService, Task> action)
    {
        (IServiceScope? scope, IWorkflowOutputService? service) = _apiBuilder.GetNewScope<IWorkflowOutputService>();
        using (scope)
        {
            await action(service);
        }
    }

    private async Task<string> SeedExecutionAsync(
        string organizationId,
        WorkflowState state,
        DateTime? startedAt = null)
    {
        string id = Guid.NewGuid().ToString();
        _seededExecutionIds.Add(id);

        (IServiceScope? scope, IUserDataUnitOfWork? uow) = _apiBuilder.GetNewScope<IUserDataUnitOfWork>();
        using (scope)
        {
            var execution = new WorkflowExecution
            {
                Id = id,
                Title = "Terminal State Test Execution",
                OrganizationId = organizationId,
                UserId = "test-user-id",
                WorkflowState = state,
                StartedAt = startedAt,
            };
            await uow.WorkflowExecutions.AddAsync(execution);

            _ = await uow.CompleteAsync();
        }

        return id;
    }

    private async Task<WorkflowExecution?> ReloadAsync(string id)
    {
        (IServiceScope? scope, IUserDataUnitOfWork? uow) = _apiBuilder.GetNewScope<IUserDataUnitOfWork>();
        using (scope)
        {
            return await uow.WorkflowExecutions.SingleOrDefaultAsync(predicate: x => x.Id == id);
        }
    }
}
