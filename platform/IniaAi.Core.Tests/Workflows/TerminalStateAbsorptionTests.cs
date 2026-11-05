using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// Guards that a terminal execution state absorbs every later write from the engine: the run's own
/// success and progress writes are conditional on the row still being active, so a cancel that lands
/// mid-run can never be overwritten by a late Draft, and no write can move a finished row backwards.
/// </summary>
public class TerminalStateAbsorptionTests
    : IClassFixture<WorkflowsFixture<TerminalStateAbsorptionTests>>, IDisposable
{
    private const string ORG_ID = TestWorkflowRuns.ORG_ID;
    private const string USER_ID = TestWorkflowRuns.USER_ID;

    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly WorkflowHandler _workflowHandler;

    public TerminalStateAbsorptionTests(
        WorkflowsFixture<TerminalStateAbsorptionTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(testOutputHelper);
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _workflowHandler = _testServiceProvider.GetRequiredService<WorkflowHandler>();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceScope.Dispose();
    }

    [Fact]
    public async Task DraftWrite_AfterAnExternalCancel_LeavesTheRowCancelled_AndReportsTheLoss()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedExecutionAsync(WorkflowState.Running, ct);
        await BootstrapHandlerAsync(seeded);

        await TestWorkflowRuns.MarkTerminalFromOutsideAsync(_testServiceProvider, seeded.Id, WorkflowState.Cancelled);

        _ = await Assert.ThrowsAsync<WorkflowExecutionFencedException>(
            () => _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Draft, _userDataUnitOfWork));

        WorkflowExecution? reloaded = await TestWorkflowRuns.ReloadExecutionAsync(_testServiceProvider, seeded.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Cancelled, reloaded.WorkflowState);
    }

    [Theory]
    [InlineData(WorkflowState.Running)]
    [InlineData(WorkflowState.ProcessingDocuments)]
    public async Task ProgressWrite_AgainstATerminalRow_LeavesItUnchanged(WorkflowState progressState)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedExecutionAsync(WorkflowState.Failed, ct);
        await BootstrapHandlerAsync(seeded);

        _ = await Assert.ThrowsAsync<WorkflowExecutionFencedException>(
            () => _workflowHandler.UpdateWorkflowStateAsync(progressState, _userDataUnitOfWork));

        WorkflowExecution? reloaded = await TestWorkflowRuns.ReloadExecutionAsync(_testServiceProvider, seeded.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Failed, reloaded.WorkflowState);
    }

    [Fact]
    public async Task ProgressWrite_AgainstAnActiveRow_WinsAndStampsStartedAt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedExecutionAsync(WorkflowState.Queued, ct);
        await BootstrapHandlerAsync(seeded);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        WorkflowExecution? reloaded = await TestWorkflowRuns.ReloadExecutionAsync(_testServiceProvider, seeded.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Running, reloaded.WorkflowState);
        Assert.NotNull(reloaded.StartedAt);
        Assert.Null(reloaded.CompletedAt);
    }

    [Fact]
    public async Task ReplayUnmatchedWrite_AgainstACancelledRow_DoesNotResurrectIt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedExecutionAsync(WorkflowState.Running, ct);
        await BootstrapHandlerAsync(seeded);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);
        await TestWorkflowRuns.MarkTerminalFromOutsideAsync(_testServiceProvider, seeded.Id, WorkflowState.Cancelled);

        await _userDataUnitOfWork.WorkflowExecutions.UpdateReplayOutcomeAsync(
            seeded.Id,
            ORG_ID,
            "[{\"Reason\":\"uncovered\"}]",
            freezeReplayedCount: 0,
            freezeEligibleCount: 1,
            cancellationToken: ct);

        WorkflowExecution? reloaded = await TestWorkflowRuns.ReloadExecutionAsync(_testServiceProvider, seeded.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Cancelled, reloaded.WorkflowState);
        Assert.NotNull(reloaded.ReplayUnmatchedJson);
    }

    [Fact]
    public async Task CounterpartyIdWrite_AgainstACancelledRow_LandsWithoutResurrectingIt()
    {
        const string LOCATED_COUNTERPARTY_ID = "cp-located-mid-run";

        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedExecutionAsync(WorkflowState.Running, ct);
        await BootstrapHandlerAsync(seeded);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);
        await TestWorkflowRuns.MarkTerminalFromOutsideAsync(_testServiceProvider, seeded.Id, WorkflowState.Cancelled);

        await _userDataUnitOfWork.WorkflowExecutions.UpdateCounterpartyIdAsync(
            seeded.Id,
            ORG_ID,
            LOCATED_COUNTERPARTY_ID,
            ct);

        WorkflowExecution? reloaded = await TestWorkflowRuns.ReloadExecutionAsync(_testServiceProvider, seeded.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Cancelled, reloaded.WorkflowState);
        Assert.Equal(LOCATED_COUNTERPARTY_ID, reloaded.CounterpartyId);
    }

    private async Task<WorkflowExecution> SeedExecutionAsync(
        WorkflowState state,
        CancellationToken cancellationToken)
    {
        WorkflowExecution execution = new()
        {
            Id = Guid.NewGuid().ToString(),
            WorkflowId = Guid.NewGuid().ToString(),
            Title = "Absorption Test Execution",
            OrganizationId = ORG_ID,
            UserId = USER_ID,
            WorkflowState = state,
        };

        await _userDataUnitOfWork.WorkflowExecutions.AddAsync(execution);
        _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);
        return execution;
    }

    private Task BootstrapHandlerAsync(WorkflowExecution seeded)
    {
        Workflow workflow = new() { Id = seeded.WorkflowId! };
        return _workflowHandler.CreateRootOutputAsync(
            _userDataUnitOfWork,
            jobId: null,
            rootWorkflow: workflow,
            organizationId: ORG_ID,
            userId: USER_ID,
            workflowExecutionId: seeded.Id);
    }
}
