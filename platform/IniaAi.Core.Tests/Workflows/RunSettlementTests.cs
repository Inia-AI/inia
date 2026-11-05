using IniaAi.Common.Interfaces.Services.Runs;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// Every won flip to a final state settles the run once: the handler's completion, and the terminal
/// funnel's failure and cancellation. A flip someone else already won, and a state that is not
/// final, settle nothing. The user's cancel command is covered beside its handler.
/// </summary>
public class RunSettlementTests
    : IClassFixture<WorkflowsFixture<RunSettlementTests>>, IDisposable
{
    private const string ORG_ID = "TestOrg";
    private const string USER_ID = "TestUser";

    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly WorkflowHandler _workflowHandler;
    private readonly IWorkflowOutputService _workflowOutputService;
    private readonly RecordingRunSettlement _settlement = new();

    public RunSettlementTests(WorkflowsFixture<RunSettlementTests> fixture, ITestOutputHelper testOutputHelper)
    {
        IServiceProvider provider = fixture.BuildTestServiceProvider(
            testOutputHelper,
            services => services.AddSingleton<IRunSettlement>(_settlement));
        _serviceScope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _workflowHandler = provider.GetRequiredService<WorkflowHandler>();
        _workflowOutputService = provider.GetRequiredService<IWorkflowOutputService>();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceScope.Dispose();
    }

    [Fact]
    public async Task ARunThatReachesDraft_IsSettledOnce()
    {
        WorkflowExecution execution = await SeedExecutionAsync(WorkflowState.Queued);
        await BootstrapHandlerAsync(execution);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Draft, _userDataUnitOfWork);

        Assert.Equal([(execution.Id, ORG_ID)], _settlement.Settled);
    }

    [Fact]
    public async Task ARunThatFails_IsSettledOnce()
    {
        WorkflowExecution execution = await SeedExecutionAsync(WorkflowState.Running);

        bool marked = await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(
            execution.Id, ORG_ID, WorkflowExecutionFailureKinds.RESULTS_LOST, "a block never concluded");

        Assert.True(marked);
        Assert.Equal([(execution.Id, ORG_ID)], _settlement.Settled);
    }

    [Fact]
    public async Task ARunThatIsCancelled_IsSettledOnce()
    {
        WorkflowExecution execution = await SeedExecutionAsync(WorkflowState.Queued);

        bool marked = await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(execution.Id, ORG_ID);

        Assert.True(marked);
        Assert.Equal([(execution.Id, ORG_ID)], _settlement.Settled);
    }

    [Fact]
    public async Task AFlipAnotherWriterAlreadyWon_SettlesNothing()
    {
        WorkflowExecution execution = await SeedExecutionAsync(WorkflowState.Failed);

        bool marked = await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(execution.Id, ORG_ID);

        Assert.False(marked);
        Assert.Empty(_settlement.Settled);
    }

    [Fact]
    public async Task StatesThatAreNotFinal_SettleNothing()
    {
        WorkflowExecution execution = await SeedExecutionAsync(WorkflowState.Queued);
        await BootstrapHandlerAsync(execution);

        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.ProcessingDocuments, _userDataUnitOfWork);
        await _workflowHandler.UpdateWorkflowStateAsync(WorkflowState.Running, _userDataUnitOfWork);

        Assert.Empty(_settlement.Settled);
    }

    private async Task<WorkflowExecution> SeedExecutionAsync(WorkflowState state)
    {
        WorkflowExecution execution = new()
        {
            Id = Guid.NewGuid().ToString(),
            WorkflowId = Guid.NewGuid().ToString(),
            Title = "Settlement test",
            OrganizationId = ORG_ID,
            UserId = USER_ID,
            WorkflowState = state,
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
