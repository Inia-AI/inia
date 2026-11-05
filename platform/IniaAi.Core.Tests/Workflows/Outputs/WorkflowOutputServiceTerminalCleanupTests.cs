using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Core.Services.Models;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace IniaAi.Core.Tests.Workflows.Outputs;

/// <summary>
/// The terminal funnel owns agent-run cleanup: a won flip — Failed or Cancelled, whoever
/// asked for it — runs the cleanup exactly once, and a lost flip never does.
/// </summary>
public class WorkflowOutputServiceTerminalCleanupTests
    : IClassFixture<WorkflowsFixture<WorkflowOutputServiceTerminalCleanupTests>>, IDisposable
{
    private const string ORG_ID = "TestOrg";

    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IAgentRunTerminalCleanup _cleanup;
    private readonly WorkflowOutputService _workflowOutputService;

    public WorkflowOutputServiceTerminalCleanupTests(
        WorkflowsFixture<WorkflowOutputServiceTerminalCleanupTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(testOutputHelper);
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _cleanup = Substitute.For<IAgentRunTerminalCleanup>();
        _workflowOutputService = new WorkflowOutputService(
            _testServiceProvider.GetRequiredService<IWorkflowOutputBuilder>(),
            _testServiceProvider.GetRequiredService<IWorkflowOutputValidator>(),
            _testServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            _testServiceProvider.GetRequiredService<ILogger<WorkflowOutputService>>(),
            _cleanup);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceScope.Dispose();
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_WinsTheFlip_RunsCleanupOnce()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Running);

        bool marked = await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(
            execution.Id, ORG_ID, WorkflowExecutionFailureKinds.LIFETIME_EXCEEDED, "aged out");

        Assert.True(marked);
        await _cleanup.Received(1).CleanUpAsync(execution.Id, ORG_ID, WorkflowState.Failed);
    }

    [Fact]
    public async Task MarkExecutionAsCancelledIfActiveAsync_WinsTheFlip_RunsCleanupOnce()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Running);

        bool marked = await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(
            execution.Id, ORG_ID);

        Assert.True(marked);
        await _cleanup.Received(1).CleanUpAsync(execution.Id, ORG_ID, WorkflowState.Cancelled);
    }

    [Fact]
    public async Task MarkExecutionAsCancelledIfActiveAsync_RowAlreadyTerminal_NeverRunsCleanup()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Failed);

        bool marked = await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(
            execution.Id, ORG_ID);

        Assert.False(marked);
        await _cleanup.DidNotReceiveWithAnyArgs().CleanUpAsync(default!, default!, default);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_MissingIds_ReturnsFalseWithoutCleanup()
    {
        bool marked = await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(null, ORG_ID);

        Assert.False(marked);
        await _cleanup.DidNotReceiveWithAnyArgs().CleanUpAsync(default!, default!, default);
    }

    private async Task<WorkflowExecution> CreateExecutionAsync(WorkflowState state)
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = ORG_ID,
            Title = "Terminal cleanup test",
            WorkflowState = state,
        };

        await _userDataUnitOfWork.WorkflowExecutions.AddAsync(execution);
        _ = await _userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        return execution;
    }
}
