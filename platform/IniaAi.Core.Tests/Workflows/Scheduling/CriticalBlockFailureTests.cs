using System.Collections.Concurrent;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// Guards the critical-block contract: a block whose failure must not be absorbed persists its own
/// failure record, aborts the run through the linked token, and surfaces as a typed failure carrying
/// the kind and detail the job layer writes onto the execution. A non-critical failure keeps the
/// absorb-and-continue behaviour, and an external cancel still wins the tie.
/// </summary>
public class CriticalBlockFailureTests : IClassFixture<WorkflowsFixture<CriticalBlockFailureTests>>
{
    private const int CHAIN_LENGTH = 4;
    private const string ID_PREFIX = "cb";
    private const string CRITICAL_BLOCK = "C1";
    private const string BLOCK_FAILURE = "agent start refused";

    private readonly IServiceProvider _sp;

    public CriticalBlockFailureTests(
        WorkflowsFixture<CriticalBlockFailureTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task CriticalBlockFailure_AbortsTheRun_AndCarriesKindAndDetail()
    {
        List<string> executedBlocks = [];
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: block => FailBlock(executedBlocks, block, CRITICAL_BLOCK));
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH, CRITICAL_BLOCK);

        CriticalBlockFailureException ex = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.Equal(WorkflowExecutionFailureKinds.CRITICAL_BLOCK_FAILURE, ex.FailureKind);
        Assert.Contains("C1", ex.FailureDetail);
        Assert.Contains(BLOCK_FAILURE, ex.FailureDetail);
        Assert.Equal(new[] { "C0", "C1" }, executedBlocks);
    }

    [Fact]
    public async Task CriticalBlockFailure_RecordsTheReasonOnTheExecutionItself()
    {
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: block => FailBlock([], block, CRITICAL_BLOCK));
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH, CRITICAL_BLOCK);

        _ = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        await using UserDataDbContext read = await factory.CreateDbContextAsync(ct);

        WorkflowExecutionLog abortLog = Assert.Single(
            await read.Set<WorkflowExecutionLog>().AsNoTracking()
                .Where(l => l.WorkflowExecutionId == handler.WorkflowExecutionId
                    && l.SectionName == "Execution")
                .ToListAsync(ct));

        Assert.Equal(BlockExecutionStatus.Failed, abortLog.Status);
        Assert.Contains("C1", abortLog.Error);
    }

    [Fact]
    public async Task CriticalBlockFailure_PersistsTheFailureRecordOnAnUncancellableToken()
    {
        ConcurrentDictionary<string, bool> persistTokenCancellableByBlockName = new();
        ConcurrentDictionary<string, WorkflowExecutionLog?> logsByBlockName = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call =>
        {
            Block block = call.Arg<Block>();
            persistTokenCancellableByBlockName[block.Name] = call.Arg<CancellationToken>().CanBeCanceled;
            logsByBlockName[block.Name] = call.Arg<WorkflowExecutionLog?>();
            return Task.CompletedTask;
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: block => FailBlock([], block, CRITICAL_BLOCK));
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH, CRITICAL_BLOCK);

        _ = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.False(persistTokenCancellableByBlockName["C1"]);
        Assert.True(persistTokenCancellableByBlockName["C0"]);
        Assert.Equal(BlockExecutionStatus.Failed, logsByBlockName["C1"]?.Status);
    }

    [Fact]
    public async Task NonCriticalBlockFailure_StaysAbsorbed_AndTheRunFinishes()
    {
        List<string> executedBlocks = [];
        ConcurrentDictionary<string, WorkflowExecutionLog?> logsByBlockName = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call =>
        {
            logsByBlockName[call.Arg<Block>().Name] = call.Arg<WorkflowExecutionLog?>();
            return Task.CompletedTask;
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: block => FailBlock(executedBlocks, block, CRITICAL_BLOCK));
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken);

        Assert.Equal(BlockExecutionStatus.Failed, logsByBlockName["C1"]?.Status);
        Assert.Equal(new[] { "C0", "C1", "C2", "C3" }, executedBlocks);
    }

    [Fact]
    public async Task ExternalCancel_RacingACriticalFailure_SurfacesCancellation_NotTheCriticalFailure()
    {
        using CancellationTokenSource cts = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.CompletedTask);

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: block =>
        {
            if (block.Name != "C1")
            {
                return Task.CompletedTask;
            }

            cts.Cancel();
            return Task.FromException(new InvalidOperationException(BLOCK_FAILURE));
        });
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH, CRITICAL_BLOCK);

        OperationCanceledException ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, cts.Token));

        Assert.IsNotType<CriticalBlockFailureException>(ex);
    }

    [Fact]
    public async Task CriticalAbort_CancelsAnInFlightSiblingPersist_AndReplaysItInstead()
    {
        const int PARALLEL_CAP = 2;

        TaskCompletionSource siblingPersistStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentDictionary<string, int> callsByBlockName = new();

        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(async call =>
        {
            Block block = call.Arg<Block>();
            int attempt = callsByBlockName.AddOrUpdate(block.Name, 1, (_, n) => n + 1);

            if (block.Name == "P0" && attempt == 1)
            {
                // Still in flight when the critical block trips the abort, so the trip is what
                // kills this write — its result exists nowhere but memory.
                siblingPersistStarted.SetResult();
                // Waits on purpose: hangs until the critical abort cancels it, and the cap turns a missed abort into a failure.
                await Task.Delay(TimeSpan.FromSeconds(30), call.Arg<CancellationToken>());
            }
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, PARALLEL_CAP, onBlockExecuted: async block =>
        {
            if (block.Name != "P1")
            {
                return;
            }

            await siblingPersistStarted.Task;
            throw new InvalidOperationException(BLOCK_FAILURE);
        });
        Workflow workflow = TestWorkflowRuns.BuildParallelWorkflow(ID_PREFIX, 2, "P1");

        CriticalBlockFailureException ex = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.Contains("P0", ex.RecoveredSiblingResults);
        Assert.Empty(ex.LostSiblingResults);
        Assert.Equal(2, callsByBlockName["P0"]);
    }

    [Fact]
    public async Task CriticalAbort_WithASiblingResultThatCannotBeReplayed_NamesItAsLost()
    {
        const int PARALLEL_CAP = 2;

        TaskCompletionSource siblingPersistStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentDictionary<string, int> callsByBlockName = new();

        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(async call =>
        {
            Block block = call.Arg<Block>();
            int attempt = callsByBlockName.AddOrUpdate(block.Name, 1, (_, n) => n + 1);

            if (block.Name != "P0")
            {
                return;
            }

            if (attempt == 1)
            {
                siblingPersistStarted.SetResult();
                // Waits on purpose: hangs until the critical abort cancels it, and the cap turns a missed abort into a failure.
                await Task.Delay(TimeSpan.FromSeconds(30), call.Arg<CancellationToken>());
            }

            throw new InvalidOperationException("disk full");
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, PARALLEL_CAP, onBlockExecuted: async block =>
        {
            if (block.Name != "P1")
            {
                return;
            }

            await siblingPersistStarted.Task;
            throw new InvalidOperationException(BLOCK_FAILURE);
        });
        Workflow workflow = TestWorkflowRuns.BuildParallelWorkflow(ID_PREFIX, 2, "P1");

        CriticalBlockFailureException ex = await Assert.ThrowsAsync<CriticalBlockFailureException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.Contains("P0", ex.LostSiblingResults);
        Assert.Contains("P0", ex.FailureDetail);
    }

    [Fact]
    public async Task PersistenceBreakerTrippingOnTheCriticalBlocksOwnFailureRecord_ReportsResultsLost()
    {
        // C0 and C1 fail to persist (1, 2); C2's failure record is the third consecutive persist
        // failure, so the breaker trips inside the write that precedes the critical trip.
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.FromException(new InvalidOperationException("db down")));

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: block => FailBlock([], block, "C2"));
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH, "C2");

        WorkflowResultsLostException ex = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.Equal(WorkflowExecutionFailureKinds.RESULTS_LOST, ex.FailureKind);
        Assert.Contains("C2", ex.PermanentlyLostBlocks);
    }

    private static Task FailBlock(List<string> executedBlocks, Block block, string failingBlockName)
    {
        lock (executedBlocks)
        {
            executedBlocks.Add(block.Name);
        }

        return block.Name == failingBlockName
            ? Task.FromException(new InvalidOperationException(BLOCK_FAILURE))
            : Task.CompletedTask;
    }
}
