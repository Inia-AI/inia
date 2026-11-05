using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

public class WorkflowHandlerTimingTests : IClassFixture<WorkflowsFixture<WorkflowHandlerTimingTests>>, IDisposable
{
    private const string ORG_ID = "TestOrg";
    private const string USER_ID = "TestUser";

    private readonly WorkflowsFixture<WorkflowHandlerTimingTests> _fixture;
    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly WorkflowHandler _workflowHandler;

    public WorkflowHandlerTimingTests(
        WorkflowsFixture<WorkflowHandlerTimingTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testServiceProvider = _fixture.BuildTestServiceProvider(testOutputHelper);
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
    public async Task UpdateWorkflowStateAsync_StampsStartedAt_OnFirstTransitionOutOfQueued()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);

        await BootstrapHandlerAsync(seeded);

        DateTime before = DateTime.UtcNow;
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.ProcessingDocuments, _userDataUnitOfWork);
        DateTime after = DateTime.UtcNow;

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded.StartedAt);
        Assert.True(reloaded.StartedAt >= before, $"StartedAt {reloaded.StartedAt} < before {before}");
        Assert.True(reloaded.StartedAt <= after, $"StartedAt {reloaded.StartedAt} > after {after}");
        Assert.Null(reloaded.CompletedAt);
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_StampsStartedAt_OnTransitionFromQueuedDirectlyToRunning()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);

        DateTime before = DateTime.UtcNow;
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);
        DateTime after = DateTime.UtcNow;

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded.StartedAt);
        Assert.True(reloaded.StartedAt >= before, $"StartedAt {reloaded.StartedAt} < before {before}");
        Assert.True(reloaded.StartedAt <= after, $"StartedAt {reloaded.StartedAt} > after {after}");
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_DoesNotOverwriteStartedAt_OnSubsequentTransitions()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.ProcessingDocuments, _userDataUnitOfWork);
        DateTime? firstStartedAt = (await ReloadAsync(seeded.Id))!.StartedAt;
        Assert.NotNull(firstStartedAt);

        // Waits on purpose: lets the clock move so the next timestamp differs.
        await Task.Delay(20, ct);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.Equal(firstStartedAt, reloaded!.StartedAt);
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_DoesNotStampStartedAt_OnQueuedToQueued()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Queued, _userDataUnitOfWork);

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.Null(reloaded!.StartedAt);
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_StampsCompletedAt_OnDraft()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        DateTime before = DateTime.UtcNow;
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Draft, _userDataUnitOfWork);
        DateTime after = DateTime.UtcNow;

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.True(reloaded.CompletedAt >= before, $"CompletedAt {reloaded.CompletedAt} < before {before}");
        Assert.True(reloaded.CompletedAt <= after, $"CompletedAt {reloaded.CompletedAt} > after {after}");
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_StampsCompletedAt_OnFailed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        DateTime before = DateTime.UtcNow;
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Failed, _userDataUnitOfWork);
        DateTime after = DateTime.UtcNow;

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.True(reloaded.CompletedAt >= before, $"CompletedAt {reloaded.CompletedAt} < before {before}");
        Assert.True(reloaded.CompletedAt <= after, $"CompletedAt {reloaded.CompletedAt} > after {after}");
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_StampsCompletedAt_OnCancelled()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        DateTime before = DateTime.UtcNow;
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Cancelled, _userDataUnitOfWork);
        DateTime after = DateTime.UtcNow;

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Cancelled, reloaded.WorkflowState);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.True(reloaded.CompletedAt >= before, $"CompletedAt {reloaded.CompletedAt} < before {before}");
        Assert.True(reloaded.CompletedAt <= after, $"CompletedAt {reloaded.CompletedAt} > after {after}");
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_DoesNotStampCompletedAt_OnNonTerminalTransitions()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.ProcessingDocuments, _userDataUnitOfWork);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.Null(reloaded!.CompletedAt);
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_RefusesASecondTerminalTransition_AndKeepsTheOriginalCompletedAt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Draft, _userDataUnitOfWork);
        DateTime? firstCompletedAt = (await ReloadAsync(seeded.Id))!.CompletedAt;
        Assert.NotNull(firstCompletedAt);

        // Waits on purpose: lets the clock move so the next timestamp differs.
        await Task.Delay(20, ct);
        _ = await Assert.ThrowsAsync<WorkflowExecutionFencedException>(
            () => _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Failed, _userDataUnitOfWork));

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.Equal(WorkflowState.Draft, reloaded!.WorkflowState);
        Assert.Equal(firstCompletedAt, reloaded.CompletedAt);
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_StampsCompletedAt_OnConfirmed_WhenTransitioningDirectly()
    {
        // Covers paths that materialise an execution directly in Confirmed without
        // going through Draft.
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        DateTime before = DateTime.UtcNow;
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Confirmed, _userDataUnitOfWork);
        DateTime after = DateTime.UtcNow;

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.NotNull(reloaded);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.True(reloaded.CompletedAt >= before, $"CompletedAt {reloaded.CompletedAt} < before {before}");
        Assert.True(reloaded.CompletedAt <= after, $"CompletedAt {reloaded.CompletedAt} > after {after}");
    }

    // Draft → Confirmed does not go through this writer in production — the state endpoint accepts
    // only Cancelled, and Confirmed is materialised at row creation — so the engine writer refusing
    // it costs no real flow.
    [Fact]
    public async Task UpdateWorkflowStateAsync_RefusesConfirmedAfterDraft_AndKeepsTheOriginalCompletedAt()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Draft, _userDataUnitOfWork);
        DateTime? firstCompletedAt = (await ReloadAsync(seeded.Id))!.CompletedAt;
        Assert.NotNull(firstCompletedAt);

        // Waits on purpose: lets the clock move so the next timestamp differs.
        await Task.Delay(20, ct);
        _ = await Assert.ThrowsAsync<WorkflowExecutionFencedException>(
            () => _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Confirmed, _userDataUnitOfWork));

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.Equal(WorkflowState.Draft, reloaded!.WorkflowState);
        Assert.Equal(firstCompletedAt, reloaded.CompletedAt);
    }

    [Fact]
    public async Task Duration_IsPositive_AfterRunCompletes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        WorkflowExecution seeded = await SeedQueuedExecutionAsync(ct);
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);
        // Waits on purpose: lets the clock move so the next timestamp differs.
        await Task.Delay(20, ct);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Draft, _userDataUnitOfWork);

        WorkflowExecution? reloaded = await ReloadAsync(seeded.Id);
        Assert.NotNull(reloaded!.Duration);
        Assert.True(reloaded.Duration > TimeSpan.Zero, $"Duration was {reloaded.Duration}");
    }

    private async Task<WorkflowExecution> SeedQueuedExecutionAsync(CancellationToken cancellationToken)
    {
        WorkflowExecution execution = new()
        {
            Id = Guid.NewGuid().ToString(),
            WorkflowId = Guid.NewGuid().ToString(),
            Title = "Test Execution",
            OrganizationId = ORG_ID,
            UserId = USER_ID,
            WorkflowState = WorkflowState.Queued,
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

    private Task<WorkflowExecution?> ReloadAsync(string id)
    {
        return TestWorkflowRuns.ReloadExecutionAsync(_testServiceProvider, id);
    }
}
