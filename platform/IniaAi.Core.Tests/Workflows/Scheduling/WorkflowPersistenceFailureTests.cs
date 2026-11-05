using System.Collections.Concurrent;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Tests.Workflows.Scheduling.Helpers;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// Guards the persistence-failure contract of a persisted run: a run whose block results could not
/// all be persisted must throw <see cref="WorkflowResultsLostException"/> (so the job layer marks it
/// Failed) instead of concluding as finished, mid-run persist failures are retried once at end of
/// run, and the workflow/section snapshot failure surfaces on the success path. The block writer is
/// substituted per scenario; everything else is the real engine against the in-memory stores.
/// </summary>
public class WorkflowPersistenceFailureTests : IClassFixture<WorkflowsFixture<WorkflowPersistenceFailureTests>>
{
    private const int CHAIN_LENGTH = 4;
    private const string ID_PREFIX = "pf";

    private readonly IServiceProvider _sp;

    public WorkflowPersistenceFailureTests(
        WorkflowsFixture<WorkflowPersistenceFailureTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task CleanRun_PersistsEveryBlock_AndCompletes()
    {
        IExecutedBlockWriter writer = Substitute.For<IExecutedBlockWriter>();
        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken);

        await writer.Received(CHAIN_LENGTH).PersistBlockResultAsync(
            Arg.Any<Block>(),
            Arg.Any<ReportParagraph?>(),
            Arg.Any<WorkflowExecutionLog?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistFailsOnceThenRecovers_ReplaysTheBlock_AndStillFailsTheRun()
    {
        // ExecuteWorkflowAsync runs a fresh-ID clone of the workflow, so persist calls are
        // tracked by block NAME (preserved by the clone), not by entity id.
        ConcurrentDictionary<string, int> callsByBlockName = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call =>
        {
            Block block = call.Arg<Block>();
            int attempt = callsByBlockName.AddOrUpdate(block.Name, 1, (_, n) => n + 1);

            return block.Name == "C1" && attempt == 1
                ? Task.FromException(new InvalidOperationException("disk full"))
                : Task.CompletedTask;
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        WorkflowResultsLostException ex = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.Contains("C1", ex.RecoveredBlocks);
        Assert.Empty(ex.PermanentlyLostBlocks);
        Assert.Equal(2, callsByBlockName["C1"]);
    }

    [Fact]
    public async Task PersistFailsPersistently_FailsTheRun_AndNamesTheBlockAsLost()
    {
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call => call.Arg<Block>().Name == "C1"
            ? Task.FromException(new InvalidOperationException("disk full"))
            : Task.CompletedTask);

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        WorkflowResultsLostException ex = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.Contains("C1", ex.PermanentlyLostBlocks);
        Assert.Contains("C1", ex.Message);

        // The failure reason must be visible on the execution itself (Logs tab), written through a
        // fresh scope because the run's own persistence is what failed.
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        await using UserDataDbContext read = await factory.CreateDbContextAsync(ct);

        WorkflowExecutionLog lostLog = Assert.Single(
            await read.Set<WorkflowExecutionLog>().AsNoTracking()
                .Where(l => l.WorkflowExecutionId == handler.WorkflowExecutionId
                    && l.SectionName == "Execution")
                .ToListAsync(ct));

        Assert.Equal(BlockExecutionStatus.Failed, lostLog.Status);
        Assert.Contains("C1", lostLog.Error);
    }

    [Fact]
    public async Task PersistFailure_DoesNotMarkTheBlockFailedInMemory()
    {
        // The executed clone's blocks are only reachable through the writer's calls, so capture
        // the instance the writer saw and assert on its in-memory state after the run.
        ConcurrentDictionary<string, Block> seenBlocksByName = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call =>
        {
            Block block = call.Arg<Block>();
            seenBlocksByName[block.Name] = block;

            return block.Name == "C1"
                ? Task.FromException(new InvalidOperationException("disk full"))
                : Task.CompletedTask;
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        _ = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.Equal(ExecutionStatus.Completed, seenBlocksByName["C1"].ExecutionState?.Status);
    }

    [Fact]
    public async Task SnapshotPersistFails_OnOtherwiseCleanRun_FailsTheRun()
    {
        IExecutedBlockWriter writer = Substitute.For<IExecutedBlockWriter>();
        _ = writer.PersistWorkflowExecutionStateAsync(Arg.Any<Workflow>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("disk full at snapshot")));

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        WorkflowResultsLostException ex = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.NotNull(ex.SnapshotFailure);
        Assert.Contains("disk full at snapshot", ex.SnapshotFailure);
    }

    [Fact]
    public async Task ExternalCancellation_WithFailingSnapshot_SurfacesCancellation_NotResultsLost()
    {
        using CancellationTokenSource cts = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        });
        _ = writer.PersistWorkflowExecutionStateAsync(Arg.Any<Workflow>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("snapshot down")));

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, cts.Token));
    }

    [Fact]
    public async Task PersistFailsForEveryBlock_BreakerTripsAtThreshold_AndStopsExecutingBlocks()
    {
        const int LONG_CHAIN = 8;
        const int BREAKER_THRESHOLD = 3;

        int executedBlocks = 0;
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.FromException(new InvalidOperationException("db down")));

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: block =>
        {
            _ = Interlocked.Increment(ref executedBlocks);
            return Task.CompletedTask;
        });
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, LONG_CHAIN);

        WorkflowResultsLostException ex = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        // The breaker trips on the third consecutive persist failure; no further block strategies
        // may run (that would burn model tokens against a dead store).
        Assert.Equal(BREAKER_THRESHOLD, executedBlocks);
        Assert.Equal(BREAKER_THRESHOLD, ex.PermanentlyLostBlocks.Count);
        Assert.NotEmpty(ex.UnconcludedBlocks);
    }

    [Fact]
    public async Task BreakerTripOnTheFinalBlock_ReplaysTheQueuedResults_InsteadOfDroppingThem()
    {
        const int BREAKER_THRESHOLD = 3;

        ConcurrentDictionary<string, int> callsByBlockName = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call =>
        {
            Block block = call.Arg<Block>();
            int attempt = callsByBlockName.AddOrUpdate(block.Name, 1, (_, n) => n + 1);

            return attempt == 1
                ? Task.FromException(new InvalidOperationException("db down"))
                : Task.CompletedTask;
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, BREAKER_THRESHOLD);

        WorkflowResultsLostException ex = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        // The trip lands on the last dispatched block, so the scheduler finishes on its own and the
        // end-of-run replay is still reachable — the queued results get their one retry instead of
        // being reported permanently lost.
        Assert.Equal(new[] { "C0", "C1", "C2" }, ex.RecoveredBlocks.OrderBy(name => name));
        Assert.Empty(ex.PermanentlyLostBlocks);
        Assert.All(callsByBlockName.Values, calls => Assert.Equal(2, calls));
    }

    [Fact]
    public async Task ParallelRun_WithOneFailingPersist_FailsTheRun_AndReplaysExactlyOnce()
    {
        const int PARALLEL_CAP = 4;
        const int BLOCK_COUNT = 6;

        ConcurrentDictionary<string, int> callsByBlockName = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call =>
        {
            Block block = call.Arg<Block>();
            _ = callsByBlockName.AddOrUpdate(block.Name, 1, (_, n) => n + 1);

            return block.Name == "P3"
                ? Task.FromException(new InvalidOperationException("disk full"))
                : Task.CompletedTask;
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, PARALLEL_CAP);
        Workflow workflow = TestWorkflowRuns.BuildParallelWorkflow(ID_PREFIX, BLOCK_COUNT);

        WorkflowResultsLostException ex = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.Contains("P3", ex.PermanentlyLostBlocks);
        Assert.Empty(ex.RecoveredBlocks);
        Assert.Equal(2, callsByBlockName["P3"]);
        Assert.All(
            callsByBlockName.Where(entry => entry.Key != "P3"),
            entry => Assert.Equal(1, entry.Value));
    }

    [Fact]
    public async Task BreakerAbort_CancelsAnInFlightPersist_StillReportsThatBlockAsLost()
    {
        const int PARALLEL_CAP = 4;
        const int BREAKER_THRESHOLD = 3;

        TaskCompletionSource breakerAboutToTrip = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int failures = 0;

        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(async call =>
        {
            Block block = call.Arg<Block>();

            if (block.Name == "P0")
            {
                // Still in flight when the breaker trips, so the breaker's own cancellation is what
                // kills this write — its result exists nowhere but memory.
                await breakerAboutToTrip.Task;
                // Waits on purpose: hangs until the breaker cancels it, and the cap turns a missed trip into a failure.
                await Task.Delay(TimeSpan.FromSeconds(30), call.Arg<CancellationToken>());
                return;
            }

            if (Interlocked.Increment(ref failures) == BREAKER_THRESHOLD)
            {
                breakerAboutToTrip.SetResult();
            }

            throw new InvalidOperationException("db down");
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, PARALLEL_CAP);
        Workflow workflow = TestWorkflowRuns.BuildParallelWorkflow(ID_PREFIX, BREAKER_THRESHOLD + 1);

        WorkflowResultsLostException ex = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.Contains("P0", ex.PermanentlyLostBlocks);
    }

    [Fact]
    public async Task ParallelRun_PersistFailsForEveryBlock_BreakerStillAbortsTheRun()
    {
        const int PARALLEL_CAP = 4;
        const int BLOCK_COUNT = 12;
        const int BREAKER_THRESHOLD = 3;

        int executedBlocks = 0;
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ => Task.FromException(new InvalidOperationException("db down")));

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, PARALLEL_CAP, onBlockExecuted: block =>
        {
            _ = Interlocked.Increment(ref executedBlocks);
            return Task.CompletedTask;
        });
        Workflow workflow = TestWorkflowRuns.BuildParallelWorkflow(ID_PREFIX, BLOCK_COUNT);

        WorkflowResultsLostException ex = await Assert.ThrowsAsync<WorkflowResultsLostException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken));

        Assert.NotEmpty(ex.PermanentlyLostBlocks);
        Assert.InRange(executedBlocks, BREAKER_THRESHOLD, BLOCK_COUNT - 1);
    }

    [Fact]
    public async Task ExternalCancellation_UnderFailingWriter_SurfacesCancellation_NotResultsLost()
    {
        using CancellationTokenSource cts = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(_ =>
        {
            cts.Cancel();
            return Task.FromException(new InvalidOperationException("db down"));
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, cts.Token));
    }

    [Fact]
    public async Task BlockInternalTaskCanceled_WithNoTokenFired_FailsTheBlock_NotTheRun()
    {
        ConcurrentDictionary<string, WorkflowExecutionLog?> logsByBlockName = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call =>
        {
            logsByBlockName[call.Arg<Block>().Name] = call.Arg<WorkflowExecutionLog?>();
            return Task.CompletedTask;
        });

        // Simulates an HttpClient timeout inside block C1: TaskCanceledException without any
        // cancellation having been requested.
        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer, onBlockExecuted: block => block.Name == "C1"
            ? Task.FromException(new TaskCanceledException("http timeout"))
            : Task.CompletedTask);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken);

        Assert.Equal(BlockExecutionStatus.Failed, logsByBlockName["C1"]?.Status);
        Assert.True(logsByBlockName.ContainsKey("C3"), "blocks after the timed-out one must still execute");
    }

    [Fact]
    public async Task UnparseableParameterizedConfig_SkipsItsBlocks_AndDoesNotFailTheRun()
    {
        ConcurrentDictionary<string, Block> seenBlocksByName = new();
        IExecutedBlockWriter writer = TestWorkflowRuns.SubstitutedWriter(call =>
        {
            Block block = call.Arg<Block>();
            seenBlocksByName[block.Name] = block;
            return Task.CompletedTask;
        });

        WorkflowHandler handler = TestWorkflowHandlers.BuildHooked(_sp, writer);
        Workflow workflow = TestWorkflowRuns.BuildChainedWorkflow(ID_PREFIX, CHAIN_LENGTH);

        Section brokenParameterized = new("Broken", workflow) { Id = "pf-broken-sec", Order = 1 };
        brokenParameterized.ParameterizedConfig = "not-valid-json {{{";
        _ = new Block(BlockTypes.Merge, brokenParameterized, replacementTag: "pf_broken")
        {
            Id = "pf-broken-blk",
            Name = "P0",
            Properties = new MergeProperties { TextTemplate = "never expands" },
        };

        await TestWorkflowRuns.RunWithOutputAsync(_sp, handler, workflow, TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionStatus.Skipped, seenBlocksByName["P0"].ExecutionState?.Status);
    }

    [Fact]
    public void CollectUnconcludedBlocks_FlagsOnlyNonTerminalStates()
    {
        Workflow root = new() { Id = "gate-root", Title = "gate" };
        Section section = new("S", root) { Id = "gate-sec", Order = 0 };

        Block noState = NewBlock(section, "no-state");

        Block notExecuted = NewBlock(section, "not-executed");
        notExecuted.ExecutionState = new BlockExecutionState(notExecuted);

        Block running = NewBlock(section, "running");
        running.ExecutionState = new BlockExecutionState(running);
        running.ExecutionState.MarkAsStarted();

        Block completed = NewBlock(section, "completed");
        completed.ExecutionState = new BlockExecutionState(completed);
        completed.ExecutionState.MarkAsStarted();
        completed.ExecutionState.MarkAsCompleted();

        Block failed = NewBlock(section, "failed");
        failed.ExecutionState = new BlockExecutionState(failed);
        failed.ExecutionState.MarkAsFailed("boom");

        Block skipped = NewBlock(section, "skipped");
        skipped.ExecutionState = new BlockExecutionState(skipped);
        skipped.ExecutionState.MarkAsSkipped("not needed");

        List<Block> unconcluded = WorkflowHandler.CollectUnconcludedBlocks(root);

        string[] names = [.. unconcluded.Select(b => b.Name).OrderBy(n => n)];
        Assert.Equal(new[] { "no-state", "not-executed", "running" }, names);
    }

    [Fact]
    public void CollectUnconcludedBlocks_ToleratesSeparatorSections_AndFlagsNothingOnACleanWorkflow()
    {
        Workflow root = new() { Id = "sep-root", Title = "sep" };
        Section separator = new("Sep", root) { Id = "sep-sec", Order = 0 };
        separator.Properties.IsSeparator = true;

        Section content = new("S", root) { Id = "sep-content", Order = 1 };
        Block completed = NewBlock(content, "completed");
        completed.ExecutionState = new BlockExecutionState(completed);
        completed.ExecutionState.MarkAsCompleted();

        Assert.Empty(WorkflowHandler.CollectUnconcludedBlocks(root));
    }

    private static Block NewBlock(Section section, string name)
    {
        return new Block(BlockTypes.Merge, section, replacementTag: $"tag_{name}")
        {
            Id = $"blk-{name}",
            Name = name,
            Properties = new MergeProperties { TextTemplate = name },
        };
    }
}
