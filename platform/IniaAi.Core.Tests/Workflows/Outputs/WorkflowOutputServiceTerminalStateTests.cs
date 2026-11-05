using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Outputs;

public class WorkflowOutputServiceTerminalStateTests
    : IClassFixture<WorkflowsFixture<WorkflowOutputServiceTerminalStateTests>>, IDisposable
{
    private const string ORG_ID = "TestOrg";
    private const string ORIGINAL_TITLE = "Original title";

    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IWorkflowOutputService _workflowOutputService;

    public WorkflowOutputServiceTerminalStateTests(
        WorkflowsFixture<WorkflowOutputServiceTerminalStateTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(testOutputHelper);
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _workflowOutputService = _testServiceProvider.GetRequiredService<IWorkflowOutputService>();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceScope.Dispose();
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_RunningExecution_PersistsFailedWithTimestampsAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Running);

        await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(execution.Id, ORG_ID);

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowState.Failed, persisted.WorkflowState);
        Assert.NotNull(persisted.StartedAt);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task MarkExecutionAsCancelledIfActiveAsync_QueuedExecution_PersistsCancelledWithoutStartedAtAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Queued);

        await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(execution.Id, ORG_ID);

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowState.Cancelled, persisted.WorkflowState);
        Assert.Null(persisted.StartedAt);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task MarkExecutionAsCancelledIfActiveAsync_AlreadyFailed_LeavesStateUntouchedAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Failed);

        await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(execution.Id, ORG_ID);

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowState.Failed, persisted.WorkflowState);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_CallerHoldsDirtyUnsavedEntity_PersistsFailedWithoutFlushingCallerChangesAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Running);
        execution.Title = "poisoned unsaved change";

        await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(execution.Id, ORG_ID);

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowState.Failed, persisted.WorkflowState);
        Assert.Equal(ORIGINAL_TITLE, persisted.Title);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_AlreadyTerminal_LeavesStateAndCompletedAtUntouchedAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Cancelled);
        DateTime completedAt = DateTime.UtcNow.AddMinutes(-5);
        execution.CompletedAt = completedAt;
        _ = await _userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(execution.Id, ORG_ID);

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowState.Cancelled, persisted.WorkflowState);
        Assert.Equal(completedAt, persisted.CompletedAt);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_WithAClassifiedFailure_PersistsKindAndDetailAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Running);

        await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(
            execution.Id,
            ORG_ID,
            WorkflowExecutionFailureKinds.CRITICAL_BLOCK_FAILURE,
            "Block 'Agent' is critical and failed, so the execution was aborted: runtime refused the run");

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowState.Failed, persisted.WorkflowState);
        Assert.Equal(WorkflowExecutionFailureKinds.CRITICAL_BLOCK_FAILURE, persisted.FailureKind);
        Assert.Contains("runtime refused the run", persisted.FailureDetail);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_OverlongDetail_TruncatesToTheColumnLimitAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Running);
        string overlongDetail = new('x', WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH + 500);

        await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(
            execution.Id,
            ORG_ID,
            WorkflowExecutionFailureKinds.RESULTS_LOST,
            overlongDetail);

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH, persisted.FailureDetail?.Length);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_TruncationBoundaryInsideASurrogatePair_CutsBeforeItAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Running);
        string detailStraddlingTheBoundary =
            new string('x', WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH - 1) + "🙂" + new string('x', 500);

        await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(
            execution.Id,
            ORG_ID,
            WorkflowExecutionFailureKinds.RESULTS_LOST,
            detailStraddlingTheBoundary);

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowState.Failed, persisted.WorkflowState);
        Assert.Equal(WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH - 1, persisted.FailureDetail?.Length);
        Assert.False(char.IsHighSurrogate(persisted.FailureDetail![^1]));
    }

    [Fact]
    public async Task MarkExecutionAsCancelledIfActiveAsync_LeavesTheFailureColumnsEmptyAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Running);

        await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(execution.Id, ORG_ID);

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Null(persisted.FailureKind);
        Assert.Null(persisted.FailureDetail);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_UndefinedExecution_PersistsFailedAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Undefined);

        await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(
            execution.Id,
            ORG_ID,
            WorkflowExecutionFailureKinds.RESULTS_LOST,
            "never left Undefined");

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowState.Failed, persisted.WorkflowState);
        Assert.Equal(WorkflowExecutionFailureKinds.RESULTS_LOST, persisted.FailureKind);
    }

    [Fact]
    public async Task MarkExecutionAsCancelledIfActiveAsync_UndefinedExecution_PersistsCancelledAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync(WorkflowState.Undefined);

        await _workflowOutputService.MarkExecutionAsCancelledIfActiveAsync(execution.Id, ORG_ID);

        WorkflowExecution? persisted = await ReadFreshAsync(execution.Id);
        Assert.NotNull(persisted);
        Assert.Equal(WorkflowState.Cancelled, persisted.WorkflowState);
    }

    [Fact]
    public async Task MarkExecutionAsFailedIfActiveAsync_UnknownOrMissingIds_DoesNotThrowAsync()
    {
        Exception? unknownId = await Record.ExceptionAsync(
            () => _workflowOutputService.MarkExecutionAsFailedIfActiveAsync("does-not-exist", ORG_ID));
        Exception? missingExecutionId = await Record.ExceptionAsync(
            () => _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(null, ORG_ID));
        Exception? missingOrganizationId = await Record.ExceptionAsync(
            () => _workflowOutputService.MarkExecutionAsFailedIfActiveAsync("some-id", null));

        Assert.Null(unknownId);
        Assert.Null(missingExecutionId);
        Assert.Null(missingOrganizationId);
    }

    private async Task<WorkflowExecution> CreateExecutionAsync(WorkflowState state)
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = ORG_ID,
            Title = ORIGINAL_TITLE,
            WorkflowState = state,
        };

        await _userDataUnitOfWork.WorkflowExecutions.AddAsync(execution);
        _ = await _userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        return execution;
    }

    private async Task<WorkflowExecution?> ReadFreshAsync(string executionId)
    {
        using IServiceScope scope = _testServiceProvider
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope();

        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        return await unitOfWork.WorkflowExecutions
            .SingleOrDefaultAsync(predicate: x => x.Id == executionId);
    }
}
