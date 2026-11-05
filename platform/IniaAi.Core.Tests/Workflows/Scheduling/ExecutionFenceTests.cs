using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using NSubstitute;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// Guards the fencing contract: when the execution row reaches a terminal state by someone else's
/// hand — a cross-host cancel, a janitor — the engine observes it at the next block boundary, stops
/// dispatching, and writes nothing further to that row. A caller's own cancellation still wins and
/// still surfaces as a cancellation.
/// </summary>
public class ExecutionFenceTests : IClassFixture<WorkflowsFixture<ExecutionFenceTests>>
{
    private const int CHAIN_LENGTH = 4;
    private const string ID_PREFIX = "fence";
    private const string FIRST_BLOCK = "C0";

    private readonly IServiceProvider _sp;

    public ExecutionFenceTests(
        WorkflowsFixture<ExecutionFenceTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task ExecutionCancelledMidRun_StopsDispatchAtTheNextBoundary()
    {
        List<string> executedBlocks = [];
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);
        WorkflowHandler handler = null!;

        handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: async block =>
        {
            lock (executedBlocks)
            {
                executedBlocks.Add(block.Name);
            }

            if (block.Name == FIRST_BLOCK)
            {
                await TestWorkflowRuns.MarkTerminalFromOutsideAsync(_sp, handler.WorkflowExecutionId!, WorkflowState.Cancelled);
            }
        });

        WorkflowExecutionFencedException ex = await Assert.ThrowsAsync<WorkflowExecutionFencedException>(
            () => TestWorkflowRuns.RunWithOutputAsync(
                _sp, handler, TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH), TestContext.Current.CancellationToken));

        Assert.Equal(WorkflowState.Cancelled, ex.ObservedState);
        Assert.Equal(new[] { FIRST_BLOCK }, executedBlocks);
    }

    [Fact]
    public async Task FencedRun_LeavesTheExecutionRowUntouched()
    {
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);
        WorkflowHandler handler = null!;

        handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: async block =>
        {
            if (block.Name == FIRST_BLOCK)
            {
                await TestWorkflowRuns.MarkTerminalFromOutsideAsync(_sp, handler.WorkflowExecutionId!, WorkflowState.Cancelled);
            }
        });

        _ = await Assert.ThrowsAsync<WorkflowExecutionFencedException>(
            () => TestWorkflowRuns.RunWithOutputAsync(
                _sp, handler, TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH), TestContext.Current.CancellationToken));

        WorkflowExecution? reloaded = await TestWorkflowRuns.ReloadExecutionAsync(_sp, handler.WorkflowExecutionId!);
        Assert.NotNull(reloaded);
        Assert.Equal(WorkflowState.Cancelled, reloaded.WorkflowState);
        Assert.Null(reloaded.FailureKind);
        Assert.Null(reloaded.FailureDetail);
    }

    [Fact]
    public async Task FencedRun_SkipsTheFinalSnapshotWrite()
    {
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);
        WorkflowHandler handler = null!;

        handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: async block =>
        {
            if (block.Name == FIRST_BLOCK)
            {
                await TestWorkflowRuns.MarkTerminalFromOutsideAsync(_sp, handler.WorkflowExecutionId!, WorkflowState.Cancelled);
            }
        });

        _ = await Assert.ThrowsAsync<WorkflowExecutionFencedException>(
            () => TestWorkflowRuns.RunWithOutputAsync(
                _sp, handler, TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH), TestContext.Current.CancellationToken));

        await writer.DidNotReceive().PersistWorkflowExecutionStateAsync(Arg.Any<Workflow>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExternalTokenCancel_SurfacesCancellation_NotAFence()
    {
        using CancellationTokenSource cts = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: block =>
        {
            if (block.Name == FIRST_BLOCK)
            {
                cts.Cancel();
            }

            return Task.CompletedTask;
        });

        OperationCanceledException ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH), cts.Token));

        Assert.IsNotType<WorkflowExecutionFencedException>(ex);
    }
}
