using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// The successful terminal's agent-run hook. Failure and cancellation reach cleanup through the
/// guarded terminal funnel and the cancel command; a success reached nothing at all, so an
/// execution that finished held its session bundles until the retention sweep noticed them.
/// </summary>
public class WorkflowHandlerSuccessCleanupTests
    : IClassFixture<WorkflowsFixture<WorkflowHandlerSuccessCleanupTests>>, IDisposable
{
    private const string ORG_ID = "TestOrg";
    private const string USER_ID = "TestUser";

    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly WorkflowHandler _workflowHandler;
    private readonly RecordingAgentRunTerminalCleanup _cleanup = new();

    public WorkflowHandlerSuccessCleanupTests(
        WorkflowsFixture<WorkflowHandlerSuccessCleanupTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(
            testOutputHelper,
            services => services.AddSingleton<IAgentRunTerminalCleanup>(_cleanup));
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
    public async Task UpdateWorkflowStateAsync_ReachingTheSuccessfulTerminal_RunsAgentRunCleanup()
    {
        WorkflowExecution seeded = await SeedQueuedExecutionAsync();
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Draft, _userDataUnitOfWork);

        Assert.Equal([(seeded.Id, ORG_ID, WorkflowState.Draft)], _cleanup.CleanedUp);
    }

    /// <summary>
    /// Confirmed is a completed terminal too — a caller materializing an execution straight into
    /// it wins the transition, so it must reach cleanup exactly like Draft does.
    /// </summary>
    [Fact]
    public async Task UpdateWorkflowStateAsync_ReachingConfirmedDirectly_RunsAgentRunCleanup()
    {
        WorkflowExecution seeded = await SeedQueuedExecutionAsync();
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Confirmed, _userDataUnitOfWork);

        Assert.Equal([(seeded.Id, ORG_ID, WorkflowState.Confirmed)], _cleanup.CleanedUp);
    }

    [Fact]
    public async Task UpdateWorkflowStateAsync_NonTerminalTransitions_RunNoCleanup()
    {
        WorkflowExecution seeded = await SeedQueuedExecutionAsync();
        await BootstrapHandlerAsync(seeded);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.ProcessingDocuments, _userDataUnitOfWork);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        Assert.Empty(_cleanup.CleanedUp);
    }

    /// <summary>
    /// Failure and cancellation already have their own call sites, and firing here as well would
    /// settle every leaked run twice on the path the janitor drives.
    /// </summary>
    [Fact]
    public async Task UpdateWorkflowStateAsync_FailedTerminal_RunsNoCleanupFromThisWriter()
    {
        WorkflowExecution seeded = await SeedQueuedExecutionAsync();
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Failed, _userDataUnitOfWork);

        Assert.Empty(_cleanup.CleanedUp);
    }

    /// <summary>
    /// The hook is on the won transition, so an execution another writer already finished cannot
    /// release a second time.
    /// </summary>
    [Fact]
    public async Task UpdateWorkflowStateAsync_FencedBySomeoneElsesTerminal_RunsNoCleanup()
    {
        WorkflowExecution seeded = await SeedQueuedExecutionAsync();
        await BootstrapHandlerAsync(seeded);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Cancelled, _userDataUnitOfWork);

        _ = await Assert.ThrowsAsync<WorkflowExecutionFencedException>(
            () => _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Draft, _userDataUnitOfWork));

        Assert.Empty(_cleanup.CleanedUp);
    }

    private async Task<WorkflowExecution> SeedQueuedExecutionAsync()
    {
        WorkflowExecution execution = new()
        {
            Id = Guid.NewGuid().ToString(),
            WorkflowId = Guid.NewGuid().ToString(),
            Title = "Success cleanup test",
            OrganizationId = ORG_ID,
            UserId = USER_ID,
            WorkflowState = WorkflowState.Queued,
        };

        await _userDataUnitOfWork.WorkflowExecutions.AddAsync(execution);
        _ = await _userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

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
