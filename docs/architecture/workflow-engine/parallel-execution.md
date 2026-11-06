---
type: explanation
status: current
description: "Documents the whole-workflow parallel DAG scheduler: dispatch segments, graph building, layering, barriers and fences, the per-run concurrency cap, the per-block pooled writer, how a persistence failure, a critical block or a fence ends the run, cycle breaking, verification group shape, and signature-override injection. Read when blocks run in an unexpected order, a run fails with ResultsLost, CriticalBlockFailure or VerificationGroupShape, or you change how blocks are dispatched."
---

# Whole-workflow parallel DAG execution

**On this page:** Overview · Contrast with the old strictly-sequential model · The concurrency cap · Building the graph · Layering: the SchedulingView · Barriers, fences, and skip gates · Dispatch: the LevelScheduler · Per-block pooled executed-block writer · Persistence-failure contract · Critical blocks abort the run · Terminal states absorb, and a fenced engine stops · Cyclic-reference detection and breaking · Verification groups hold their shape · Parameterized-section expansion · Signature-override injection during execution · Related pages

The workflow engine executes a run as a **dependency DAG**, not a strict top-to-bottom loop. Blocks that have no data dependency on each other run **concurrently** under a per-run concurrency cap, while true producer/consumer dependencies are still honoured. This page documents the scheduler that shipped in PR #1459 (`feat/whole-workflow-parallel-dag`): how the graph is built, how blocks are layered and dispatched, the concurrency cap, the barrier/fence/skip-gate rules that pin certain blocks, cyclic-reference breaking, parameterized-section expansion, the per-block pooled writer, and signature-override injection.

## Overview

A workflow is a tree of `Section`s, and each section owns ordered `Block`s. The pre-#1459 engine walked that tree strictly sequentially — `foreach (section in workflow.Sections.OrderBy(Order)) foreach (block in section.Blocks.OrderBy(Order))` — running one block at a time. The new engine keeps that exact ordering as a **fallback** but, by default, schedules the whole workflow as one cross-section DAG so independent blocks overlap.

The pipeline is:

1. **Segment** the ordered sections into dispatch segments: a loop section (or a parameterized section left unexpanded) is a segment of its own, and loop-free sections accumulate into one segment until a fence or a skip gate closes it (`DispatchSegments.Split`, walked by `ExecuteSectionSegmentsAsync`).
2. **Build a graph** of nodes (blocks, variables, document inputs) and edges (placeholder/fence dependencies) for the run (`WorkflowGraphBuilder.Build`).
3. **Layer** the graph with a barrier-aware Kahn topological sort into a `SchedulingView` (`SchedulingViewBuilder.Build`).
4. **Dispatch** layer by layer; within a layer, up to N blocks run concurrently, gated by `IBlockConcurrencyCap` (`LevelScheduler.RunAsync`).
5. **Persist** each block's result through its own pooled DbContext (`IExecutedBlockWriter` / `WorkflowBlockPersistence`), so concurrent blocks never share a change tracker.

The whole-workflow path is orchestrated by `WorkflowHandler` (`platform/IniaAi.Core/Workflows/WorkflowHandler.cs`); the scheduling primitives live in `platform/IniaAi.Core/Workflows/Scheduling/` and the graph types in `platform/IniaAi.Core/Workflows/Graph/`.

## Contrast with the old strictly-sequential model

| Aspect | Old sequential engine | Parallel DAG scheduler |
| --- | --- | --- |
| Ordering | One `foreach` over sections then blocks | Topological layers; independent blocks overlap |
| Concurrency | One block at a time | Up to `MaxBlockParallelism` blocks per run |
| Persistence | Single shared change tracker | One pooled context per block write |
| Cross-section overlap | None | Independent blocks across sections share a layer |
| Determinism knob | Inherent | `MaxBlockParallelism = 1` reproduces it byte-for-byte |

The scheduler is explicitly designed so that setting the cap to `1` reproduces the legacy behaviour exactly — it is both the rollback / kill switch and the equivalence contract the implementation is tested against.

## The concurrency cap

`IBlockConcurrencyCap` (`platform/IniaAi.Core/Workflows/Scheduling/IBlockConcurrencyCap.cs`) bounds how many blocks run concurrently **within a single workflow execution**. It is registered **Scoped**, so each run gets its own cap; host-wide block concurrency is therefore this value times the number of concurrent workflow jobs.

```csharp
public interface IBlockConcurrencyCap
{
    int MaxParallelism { get; }
    Task AcquireAsync(CancellationToken ct);
    void Release();
}
```

The default implementation, `BlockConcurrencyCap`, is backed by a `SemaphoreSlim` sized from `PlatformSettings.MaxBlockParallelism`, read once when the scope is created:

```csharp
public BlockConcurrencyCap(IOptions<PlatformSettings> platformSettings)
{
    int max = platformSettings.Value.MaxBlockParallelism;
    if (max <= 0)
    {
        throw new ArgumentOutOfRangeException(/* must be > 0 */);
    }

    MaxParallelism = max;
    _semaphore = new SemaphoreSlim(max, max);
}
```

The default value is **4** (`PlatformSettings.MaxBlockParallelism` in `platform/IniaAi.Configuration/Platform/PlatformSettings.cs`):

```csharp
/// Maximum number of blocks that may execute concurrently within a single workflow execution.
public int MaxBlockParallelism { get; set; } = 4;
```

Key facts about the cap:

- It is read **once** when the execution scope is created; runtime changes require a process restart.
- Setting it to **1** reproduces the sequential engine — the documented kill switch / rollback.
- Because it is scoped, host-wide concurrency is `MaxBlockParallelism` times the number of concurrent jobs (`IniaAi.MaxConcurrentJobs`), not a single process-wide gate. Registering the cap as a singleton would silently make it process-wide, which is why `AddWorkflowScheduling` registers it (and the scheduler and writer) as `Scoped`.

```csharp
public static IServiceCollection AddWorkflowScheduling(this IServiceCollection services)
{
    services.TryAddTransient<IWorkflowGraphBuilder, WorkflowGraphBuilder>();
    services.TryAddScoped<IBlockConcurrencyCap, BlockConcurrencyCap>();
    services.TryAddScoped<LevelScheduler>();
    services.TryAddScoped<IExecutedBlockWriter, WorkflowBlockPersistence>();
    services.TryAddSingleton(serviceProvider => new InstalledBlockTypes(
        new BlockStrategies(serviceProvider.GetServices<BlockStrategyRegistration>()).ByType.Keys));
    return services;
}
```

## Building the graph

`WorkflowGraphBuilder.Build` (`platform/IniaAi.Core/Workflows/Graph/WorkflowGraphBuilder.cs`) turns the section-ordered blocks plus the workflow variables into a `WorkflowGraph` of nodes and edges.

```csharp
public WorkflowGraph Build(
    Dictionary<int, List<Block>> allBlocks,
    List<WorkflowVariable> variables,
    IReadOnlySet<string>? fenceBlockIds = null)
```

Node kinds are the constants in `WorkflowGraphNodeKinds`:

```csharp
public const string BLOCK = "block";
public const string INPUT_VARIABLE = "input_variable";
public const string DOCUMENT_INPUT = "document_input";
public const string INPUT_SIGNATURE = "input_signature";
public const string INPUT_SYSTEM = "input_system";
public const string INPUT_LOOP = "input_loop";
public const string PARAMETERIZED_SECTION = "parameterized_section";
public const string UNKNOWN = "unknown";
```

The graph is a projection of `WorkflowDependencyMap` (`platform/IniaAi.Core/Workflows/Dependencies/`), the one dependency rule of a workflow. The workflow editor's dependency graph (the **Graph** tab and a block's **Dependency chain**) draws the same map, so a link the graph shows is one the run waits for. `WorkflowDependencyMap.ForDispatch` builds the map of one dispatch batch and `ToSchedulingGraph` projects it.

How nodes and edges form:

- **Variables** become `INPUT_VARIABLE` source nodes keyed by their replacement tag.
- **Blocks** become `BLOCK` nodes keyed by `block.Id` (so two blocks that share a replacement tag stay distinct nodes and each runs its own skip gate). Each node carries `SectionOrder`, `Order`, `BlockType`, `ReplacementTag`, `ProducesDocument`, and `ManualBarrier`.
- **Data edges** come from placeholders, resolved in the order the runtime resolves them: the block's own TextInputs and signature fields first (they never create an edge to a block), then the counterparty, organization and peer-company entities, then block tags, then reserved and loop inputs. For every placeholder that names a block tag, an edge runs from each block carrying the tag to the reader. A reader of a tag supplied by two producers depends on **both**.
- The run looks a placeholder up by the part before the first dot, index included. Only peer companies take an index, so `{PeerCompanies[1].Name}` reads the peer-company entity while `{x[1]}` names no block and is unresolved.
- A workflow variable with a default value is always in the shared inputs, and a shared input wins over a block for the exact placeholder. So `{query}` reads such a variable even when a block is tagged `query`, while `{query.answer}` reads the block. A variable with no default is in the shared inputs only when the run is given a value, so `{query}` keeps an edge to both.
- Every input that is not a block becomes a source node keyed by its tag (`SchedulingGraphProjection.InputNode`): a signature field is `INPUT_SIGNATURE`, a loop input `INPUT_LOOP`, the reserved `InputContext` `DOCUMENT_INPUT`, any other reserved tag `INPUT_SYSTEM`, and a placeholder that nothing produces `UNKNOWN`. These are constants by dispatch time, so their edges are treated as pre-satisfied.
- **Document edges** order a block after the blocks that create the documents it reads. A producer's documents attach to a reader only after the producer finishes, so without the edge a reader scheduled first would silently get none. An edge runs from every block whose created document class is `C` to every block that reads `C`, whatever their authored order. A block reads `C` when it receives documents (BuildTopics, an agent-family block, or a block with RAG settings) and its `SupportedDocumentClasses` name `C`, or when it is a Json2Xlsx whose `InputDocumentClass` is `C`. A block whose class list is empty or `Any` takes every document and a block set to `None` takes none: neither depends on any producer, because "Any" readers are written for the run's uploaded documents. The rule is `Block.ReadsDocumentsCreatedBy`, shared with `Workflow.IsolateBlockWithDependencies`.
- **Counterparty edges** order a block that reads a counterparty value after the block that writes it back (`ShouldUpdateCounterpartyWithResult`). The edge is key-matched: a writer of property `Style` feeds readers of `{Counterparty.Style}`, and a `LastFinancialAnalysisResult` writer feeds readers of `{Counterparty.LastFinancialAnalysisResult}`. `{Counterparty}` and `{Counterparty.Name}` read the name, which no write-back changes, so they gain no edge. A reader that already feeds the writer reads the value stored before the run and gains no edge either.
- Document and counterparty edges are added only where they close no cycle, so a workflow that scheduled without a forced cycle break still does. A reader whose edge would close a cycle runs as it did before the edges existed.
- **Fence edges** are a scheduling mechanic, described below. Section gate edges are not in the scheduling graph: gated sections are ordered by dispatch segment.


Blocks in different dispatch segments never share a graph, so no edge can make a reader in an earlier segment wait for a producer in a later one. The editor's dependency graph reports such a pair as an issue instead of drawing an edge (`DependencyIssueKind.ReadsDocumentsBeforeProducer` and `ReadsCounterpartyBeforeWriter`), worded `<reader> runs before <producer>, so it gets none of the <class> documents that block creates` and `<reader> reads {<placeholder>} before <writer> writes it`.

Source-node edges are deliberately excluded from the layering pass, so a block whose only dependencies are variables or document inputs lands in the **first** wave rather than a barrier behind every dependency-free block.

## Layering: the SchedulingView

`SchedulingViewBuilder.Build` (`platform/IniaAi.Core/Workflows/Scheduling/SchedulingViewBuilder.cs`) is a pure (no I/O, no async) transform from a `WorkflowGraph` into a `SchedulingView`. It performs Kahn topological layering with barrier collapse.

```csharp
public sealed record SchedulingView(
    IReadOnlyList<SchedulingLayer> Layers,
    IReadOnlyList<string> SequentialOrder)
{
    public IReadOnlyList<string> ForcedCycleNodes { get; init; } = [];
}

public sealed record SchedulingLayer(IReadOnlyList<string> BlockIds);
```

- **`Layers`** is the layered topological view used when the cap is `>= 2`. Every block in layer N completes before any block in layer N+1 starts; within a layer, blocks are independent and may run concurrently. Layers may span multiple sections, which is what lets cross-section independent blocks overlap.
- Within each layer, block IDs are sorted by `(SectionOrder, Order, Id)` so that the cap=1 order matches the historical sequential `foreach`.
- **`SequentialOrder`** is a priority-aware topological emission of every block ID, used as the single-threaded fallback when the cap is `1`. It is computed with a min-heap keyed by `(SectionOrder, Order, Id)` that still respects every DAG predecessor edge, reproducing the legacy `foreach` byte-for-byte for hand-authored workflows.

## Barriers, fences, and skip gates

Three distinct mechanisms keep specific blocks from being reordered or parallelised.

### Barriers (run-alone layers)

A **barrier** node occupies its own single-block layer. `SchedulingViewBuilder.SplitOnBarriers` walks each raw layer and emits any barrier as its own layer. `IsBarrier` returns true for:

- A block that **produces an output document** (`node.ProducesDocument`) — it writes the shared `AllPages` list during execution, so it must not run alongside another document producer.
- A block the author flagged to **run alone** (`node.ManualBarrier`, from `Block.RunsAlone`).
- Specific block types: `IntelligentSearch`, `Json2Xlsx`, `BuildTopicsForAllDocuments`, `LoopStart`, and `LoopEnd`.

```csharp
return node.BlockType switch
{
    BlockTypes.IntelligentSearch => true,
    BlockTypes.Json2Xlsx => true,
    BlockTypes.BuildTopicsForAllDocuments => true,
    BlockTypes.LoopStart => true,
    BlockTypes.LoopEnd => true,
    _ => false,
};
```

Loop sections are also barriers at the **segment** level: `ExecuteSectionSegmentsAsync` runs each loop-bearing section on its own (expanding it at runtime and fanning the iterations out under the cap), and groups only the loop-free sections into a shared parallel run.

`DispatchSegments.Split` owns that division. It is the one definition of where a segment ends: the dispatcher reads it, and so does any save-time guard — a second copy in a guard would drift from the dispatcher and start accepting shapes that fail at run time. It takes the fence rule as a parameter, because `IsExecutionOrderFence` is virtual on the handler: a save-time caller can only supply the base rule (`DispatchSegments.IsBaseExecutionOrderFence`), so what it sees is a floor, not the final answer.

### Fences (pin to authored position)

A **fence** block must keep its exact authored position because its side effects are invisible to placeholder analysis (it writes dynamic `SharedInputs` keys, `CounterpartyId`, or `AllPagesTopics` that later blocks may read through keys the graph cannot see). `WorkflowHandler.IsExecutionOrderFence` identifies fences (in the base handler, `BuildTopicsForAllDocuments`; a handler subclass may declare more), and `SchedulingGraphProjection.AddFenceEdges` pins each one by adding an edge from **every earlier-authored block to the fence** and from **the fence to every later-authored block**:

```csharp
if (other < fenceIndex)
{
    edges.Add(authoredOrder[other], fenceId, FENCE_LABEL);
}
else
{
    edges.Add(fenceId, authoredOrder[other], FENCE_LABEL);
}
```

Segment accumulation also closes a parallel run early when it contains a fence, so a fence's side effects are visible to the next section's skip gate.

### Skip gates

Every block (and section) carries its own **skip gate** that is evaluated at dispatch time, not pre-baked into the graph. Because block nodes are keyed by `block.Id` rather than by their shared tag, two blocks sharing a replacement tag remain distinct nodes and each evaluates its own gate. At the section-segment level, a parallel run is closed early when the next section's gate reads a tag produced inside the still-pending run (`DispatchSegments`' gate check), so the next section's prelude sees resolved outputs rather than no-data.

When a block's gate returns a skip reason, the runner marks it skipped and persists the skipped state with a log row (the skip reasons are listed on [Execution Context](execution-context.md#when-a-block-or-section-is-skipped)):

```csharp
if (blockSkipReason is not null)
{
    block.ExecutionState ??= new BlockExecutionState(block);
    if (block.ExecutionState.Status is not ExecutionStatus.Skipped)
    {
        block.ExecutionState.MarkAsSkipped(blockSkipReason);
    }

    if (RootOutput?.Id is not null)
    {
        await PersistBlockResultGuardedAsync(
            block, null, BuildSkippedLog(section, blockSkipReason, block), cancellationToken);
    }
}
```

## Dispatch: the LevelScheduler

`LevelScheduler.RunAsync` (`platform/IniaAi.Core/Workflows/Scheduling/LevelScheduler.cs`) executes a `SchedulingView`. It has two paths.

**Cap == 1 (sequential fallback):** dispatch in `SequentialOrder`, one block at a time. Serialisation comes from the awaited loop itself; the cap is honoured one level down, per unit of work, so at cap 1 that one-permit semaphore still bounds any unit a block dispatches for itself.

```csharp
if (_cap.MaxParallelism == 1)
{
    await RunSequentialAsync(view, runBlock, ct).ConfigureAwait(false);
    return;
}
```

**Cap >= 2 (layered parallel):** for each layer, start every block as a task and await `Task.WhenAll`. The scheduler does not take the cap itself — the permit binds around each billable unit of work instead (see [Where the concurrency permit binds](#where-the-concurrency-permit-binds)).

```csharp
foreach (SchedulingLayer layer in view.Layers)
{
    ct.ThrowIfCancellationRequested();

    Task[] tasks = [.. layer.BlockIds
        .Select(id => RunNodeAsync(id, runBlock, ct))];

    await Task.WhenAll(tasks).ConfigureAwait(false);
    // (error handling elided)
}

private static async Task RunNodeAsync(string blockId, Func<string, CancellationToken, Task> runBlock, CancellationToken ct)
{
    // The layer's Task[] is materialised before the try that guards it, so a delegate throwing
    // synchronously would escape uncounted. Awaiting it here turns that into a faulted task.
    await runBlock(blockId, ct).ConfigureAwait(false);
}
```

### Where the concurrency permit binds

The permit is taken around **one billable unit of work**, not around a whole scheduler node. `IBlockUnitPermit` (`platform/IniaAi.Core/Workflows/Scheduling/IBlockUnitPermit.cs`) is the engine's only acquire site, and `WorkflowHandler.RunResolvedBlockAsync` brackets each permitted block's shell in it.

This exists so a block can dispatch billable work of its own: several legs, all inside its own scheduler node. If the node held a permit while awaiting those legs, each leg would wait on a permit its own node owns — at `MaxBlockParallelism` 1 an immediate deadlock, and at cap N the same deadlock once a layer holds N such nodes. `IBlockConcurrencyCap.AcquireAsync` has no timeout, so the run would hang until the janitor reaped it.


A strategy that dispatches its own units says so through `IBlockStrategy.DispatchesNestedUnits(Block)`. The engine then runs that node with **no** permit and every unit it starts takes one of its own. Starting a unit from inside a unit throws immediately rather than waiting.

What the permit brackets did not change: it covers the same region the old node guard covered, including the fence probe and the failure-path persist. The fence probe stays inside deliberately — it opens a service scope and a database connection at every block boundary and **fails open**, so leaving it uncapped would let a wide layer exhaust the connection pool and silently stop fencing while the run kept spending. The trade a nested-dispatch node makes is stated on `IBlockUnitPermit`: its whole shell runs unpermitted, so what stays bounded is the spending, not the one fence read and child scope per orchestrating node.

Block-level exceptions are expected to be caught inside `runBlock` (catch-log-continue); the scheduler's outer catch is belt-and-braces for runner-shell escapes, and it specifically surfaces cancellation first so a sibling's regular exception can never mask an `OperationCanceledException`. Escaped non-cancellation exceptions are **counted, not just logged**: `RunAsync` returns the escape count, and the handler folds it into the end-of-run integrity gate (see [Persistence-failure contract](#persistence-failure-contract)) — an escaped failure means the block's failure handling itself failed, so its persisted state cannot be trusted and the run must not conclude as finished.

`WorkflowHandler` wires its per-block runner into the scheduler. `RunResolvedBlockAsync` takes the permit and calls `RunResolvedBlockUnitAsync`, the catch-log-continue shell around `ExecuteBlockAsync`; it persists the failed block (with an error log) on the error path, and every write goes through the per-block pooled writer. A block-internal `TaskCanceledException` with no token fired (e.g. an HttpClient timeout inside a block) is treated as a block **failure**, not a cancellation — only an abort where the run's token actually fired propagates as `OperationCanceledException`. Every dispatch path calls the scheduler through `RunSchedulerAsync`, which folds the escaped-failure count into the end-of-run gate:

```csharp
SchedulingView view = BuildSchedulingView(graph);

await RunSchedulerAsync(
    view,
    (blockId, blockCt) => blockMap.TryGetValue(blockId, out var entry)
        ? RunResolvedBlockAsync(entry.Section, entry.Block, anonymisationMap, userDataUnitOfWork, entry.Context, dispatchedBlocks, blockCt)
        : Task.CompletedTask,
    cancellationToken);

// RunSchedulerAsync:
int escapedFailures = await _scheduler.RunAsync(view, runBlock, cancellationToken);
_ = Interlocked.Add(ref _schedulerEscapedBlockFailures, escapedFailures);
```

## Per-block pooled executed-block writer

Under parallel execution a single shared `UserDataDbContext` would suffer a DetectChanges-vs-mutation race, because sibling blocks would mutate the same change tracker concurrently. The engine avoids this by detaching the executed graph before the run (`userDataUnitOfWork.ClearChangeTracker()`) and giving each block its own short-lived pooled context to write through.

`IExecutedBlockWriter` (`platform/IniaAi.Core/Workflows/Scheduling/WorkflowBlockPersistence.cs`) is the only writer of block state during the run:

```csharp
public interface IExecutedBlockWriter
{
    Task PersistBlockResultAsync(Block block, ReportParagraph? paragraph, WorkflowExecutionLog? executionLog, CancellationToken cancellationToken);
    Task PersistWorkflowExecutionStateAsync(Workflow workflow, CancellationToken cancellationToken);
    Task InsertLoopBlockAsync(Block block, CancellationToken cancellationToken);
}
```

Implementation details that matter:

- Each call leases a pooled context (`_accessor.CreateDbContextAsync()`) and disposes it in a `finally`, returning it to the pool.
- It uses `db.Entry(block).State = EntityState.Unchanged` and marks only the modified scalar properties on the block (`ExecutionState`, `Properties`, `BlockResponseId`) via `Entry`, rather than `Attach`, so EF does not walk the block's navigation graph and cascade-attach the parent `Workflow`/`Section` into this write.
- It still persists the block's **own child rows** in the same call: `PersistBlockResultAsync` loops over `block.TextInputs` and `block.RagSettings`, marking each row `Unchanged` and flagging its `ExecutionState` as modified. These carry the resolved inputs, and nothing else writes them after the shared tracker is cleared. The parent `Workflow` and `Section` rows are the genuinely-untouched ones here — they are written separately by `PersistWorkflowExecutionStateAsync`.
- It adds the block's run evidence in the same save, deduplicated on each row's natural key so a replayed write never inserts a second copy: agent run trace files (run execution id, attempt, path), agent run outputs (block, round), verification criterion records (run execution id), and the block's one citation trace row (block).
- `BlockResponse` is **upserted keyed on `BlockId`** (the table is unique on `BlockId`, not the PK), so re-execution updates the existing row in place instead of inserting a duplicate and hitting a unique-constraint violation.
- The block's report paragraph and execution log row **replace** any earlier rows with the same block id, so a replayed or re-entering write keeps one paragraph and one log row per block: the last write wins.
- Because this is the only writer of block state, a swallowed failure would silently lose a block's result; `PersistBlockResultAsync` rethrows. The handler's guarded wrapper (`PersistBlockResultGuardedAsync`) catches that rethrow, queues the exact write for an end-of-run retry, and fails the run at the end — see the persistence-failure contract below.

At the end of a run, `WorkflowBlockPersistence.PersistWorkflowExecutionStateAsync` writes the workflow-level and per-section `ExecutionState` snapshots. The shared tracker was cleared, so nothing else writes them. If this final snapshot write fails on an otherwise-successful run, the failure is surfaced (the run ends `Failed`) instead of being swallowed — stale section states must not masquerade as a finished execution.

## Persistence-failure contract

Prior to this contract, a mid-run persistence outage (e.g. DB storage full) could conclude a run as **finished with silently missing results**: per-block persist failures were retried into the same broken store, the second failure escaped into the scheduler's belt-and-braces catch and was swallowed, and `JobExecutor` marked the execution `Draft` because `ExecuteWorkflowAsync` never threw. The contract that prevents this (all in `WorkflowHandler`):

1. **Guarded persist + taint queue.** Every block-result write goes through `PersistBlockResultGuardedAsync`. On failure the exact write (`block`, `paragraph`, `log`) is queued in memory and the run continues — downstream blocks read their inputs from the in-memory graph, so execution is unaffected mid-run.
2. **End-of-run retry (forensics, not absolution).** At the end of the run every queued write is retried once through a fresh pooled context, deduped by block ID. If the store has recovered (the incident timeline), the full results — block states, responses, report paragraphs — land in UserData, so the failed execution is completely inspectable.
3. **Any anomaly fails the run.** If the queue was ever non-empty (even when the retry recovered every entry), or the completeness gate (`CollectUnconcludedBlocks`) finds blocks with no terminal state (`NotExecuted`/`Running`), or the scheduler reported escaped failures, the run throws `WorkflowResultsLostException` — `JobExecutor`'s generic error handling then marks the execution **Failed**. A Failed run is rerunnable; a silently incomplete Draft is not trusted.
4. **Persistence circuit breaker.** Three *consecutive* block-persist failures (`PERSISTENCE_BREAKER_THRESHOLD`; the counter resets on any success, so a single poisoned block never trips it) abort the run. A failed mid-run checkpoint write (`PersistBlockCheckpointGuardedAsync`, the start-time write an agent-family block makes before it spends) counts toward the same breaker but is never queued, because it carries no result; it rethrows, and the block does not start its run. The breaker aborts the run via an internal linked `CancellationTokenSource` — executing further blocks would burn model tokens on results that cannot be saved. The abort is translated to `WorkflowResultsLostException` (not a cancellation), so the run ends `Failed`, never `Cancelled`. External cancellation always wins and still surfaces as `OperationCanceledException`. One edge keeps the retry: when the trip lands on the **last dispatched block**, there is nothing left to cut short — the scheduler finishes on its own and the run falls through to the end-of-run retry, so the queued results still get their one attempt (the run ends `Failed` either way, naming what was recovered versus lost).
5. **Execution-visible trace.** Before throwing, the handler appends a Failed `WorkflowExecutionLog` row (section `Execution`, block `Result persistence`) through a fresh DI scope — `WorkflowOutputService.AppendExecutionErrorLogAsync` — so the reason survives on the execution's Logs tab even when application logs are gone.
6. **Unexpandable parameterized sections skip visibly.** A parameterized section whose config cannot be parsed (or has no variants) marks its blocks `Skipped` with the config error instead of leaving them `NotExecuted` — a config bug reads as skipped blocks, not as lost results.

The contract is test-guarded by `platform/IniaAi.Core.Tests/Workflows/Scheduling/WorkflowPersistenceFailureTests.cs`.

## Critical blocks abort the run

A block failure is **absorbed by default**: the per-block runner (`RunResolvedBlockAsync`) marks the block `Failed`, persists it with its error log, and the run continues to the next block. That is right for a block whose output is one paragraph among many, and wrong for a block that carries the run's deliverable — an Agent block's failure would otherwise leave a `Draft` report with a hole in it, having already spent everything upstream of it.

`BlockProperties.IsCritical` decides which it is. It is a nullable bool riding the existing `Properties` JSON, so it needs no migration and every template authored before it existed reads as null. `Block.AbortsExecutionOnFailure` resolves null against the block type: **Agent blocks are critical by default, every other type is not**, and an explicit `IsCritical` overrides in either direction. Editors set it per block under **Advanced settings** › **On failure**, or for every block at once from the editor's **More actions** menu (**Set every block to stop the run**).

Three block types complete over their own failure unless they are critical: Merge JSON with no valid input, String Manipulation whose operation throws, and Dynamic Chart with no point to plot. When critical, each sets its error, so the block fails and the run ends `Failed` with `CriticalBlockFailure` ([Block reference](../../guides/blocks/block-reference.md)).

An exception that implements `IAbortsWorkflowRun` aborts the run whatever the block's criticality. It is reserved for a verdict-driven stop, never an infrastructure error. The abort carries the exception's own kind and detail instead of `CriticalBlockFailure`.


When a critical block fails, the sequence is ordered so the failure record survives the abort it triggers:

1. **Persist first, on an uncancellable token.** The failing block's state and error log are written with `CancellationToken.None`, because the trip in the next step cancels the run token and would otherwise kill the very write the abort exists to leave behind.
2. **Set the discriminator, then trip.** `TripCriticalBlockAbort` latches a `CriticalBlockFailure` record (block name, explanation) under a lock — two critical blocks failing in the same parallel layer race the latch, and the first one in names the abort — and only then cancels the run-scoped linked `CancellationTokenSource`, the same abort channel the persistence breaker uses.
3. **Convert the unwind into a typed failure.** The cancellation that reaches `ExecuteWorkflowAsync` is caught by a filter that tests the caller's token first, so an **external cancel always wins the tie** and still surfaces as `OperationCanceledException` → `Cancelled`. Only an abort the caller did not ask for becomes `CriticalBlockFailureException`.
4. **Replay the siblings.** Unlike the breaker, the store is healthy here, so every sibling write the abort cancelled is queued (the enqueue-on-cancel predicate covers any internal abort, not just the breaker) and replayed once. Whatever the replay recovers or loses is named in the exception's message rather than dropped.
5. **Fail the execution with a kind and a detail.** `CriticalBlockFailureException` reaches `JobExecutor.HandleJobError`, which reads `IWorkflowExecutionFailure` off the exception and passes the kind and detail into the guarded terminal write, landing them on `WorkflowExecutions.FailureKind` / `FailureDetail` ([UserData reference](../../reference/database/user-data-database.md#workflowexecutions)). The reason is also appended as a Failed `WorkflowExecutionLog` row, so it reads on the execution's Logs tab. Both columns surface in the app on the WebApp's **Executed Workflows** list — a **State** column, and an **Attention** column that shows a Failed row's kind with a one-line summary and opens the full detail in a dialog — and on the execution-list API as `failureKind` / `failureDetail` ([Reports API](../../reference/api/reports.md#list-reports)); the list reads the database, so a run failed by the janitor after its host died still reports its state and kind.

**The breaker outranks a critical failure** when both trip in the same run (reachable: a critical block's own failure record can be the third consecutive persist failure). A dead store means lost results, the worse thing to report, so the run surfaces `WorkflowResultsLostException` and the kind `ResultsLost`.

The run's top-level checkpoints observe the linked run token rather than the caller's, so an internal abort is seen at the top level and not only inside the scheduler.

This contract is test-guarded by `platform/IniaAi.Core.Tests/Workflows/Scheduling/CriticalBlockFailureTests.cs`, with the criticality resolution itself in `platform/IniaAi.Domain.Tests/Data/Workflows/BlockTests.cs`.

## Terminal states absorb, and a fenced engine stops

An execution's status is only ever written conditionally. Every write the engine makes about its own run — `ProcessingDocuments`, `Running`, and the concluding `Draft` — goes through `TryTransitionStateIfActiveAsync`, whose `UPDATE` carries the active-state check in its `WHERE` clause and reports how many rows it changed. Zero rows means the row is no longer active, and the engine treats that as **fencing**: someone else — a user cancelling from another host, a janitor — already published a terminal state, so this run has lost the row.

This closes two holes at once. A late `Draft` can no longer overwrite a `Cancelled` that landed while the last block was running, and a progress write can no longer move a finished row backwards out of a terminal state. The old unguarded writer (a full-entity `Update` by primary key with no concurrency check) is gone rather than deprecated, so there is no longer a way to write an execution's state unconditionally.

A run that finishes can still carry a warning. When the run concludes as `Draft`, `UpdateWorkflowStateAsync` writes the first warning a group strategy reports (`IBlockGroupStrategy.CompletionWarningFor`) into `WorkflowExecutions.WarningKind` / `WarningDetail` in the same guarded transition, and the **Attention** column shows it on the finished row. Reaching a completed state also runs the agent-run cleanup (`CleanUpAgentRunsAtCompletionAsync`) where the agent runtime is enabled.


**A fenced run records nothing about itself.** Not a state, not a failure kind, not an execution log row. Any write would contradict what the winner published, and the row is no longer this run's to describe. That makes fencing the one abort that outranks the others: its catch filter sits ahead of the persistence breaker's and the critical-block abort's, so even a run that was simultaneously failing for its own reasons stops quietly. The job layer catches `WorkflowExecutionFencedException`, logs it, and finishes the job — its `MarkExecutionAs…IfActive` calls would be no-ops against a terminal row anyway, which is the guarded path doing its job.

Fences are noticed in two places:

- **Losing a conditional write**, as above.
- **At every block boundary.** Before each block runs, `FenceIfExecutionWentTerminalAsync` re-reads the execution's stored state (`GetStoredStateAsync`, untracked, so a context that loaded the row earlier still sees what other writers published). A terminal read trips the abort exactly like an external cancellation. Without it a cross-host cancel was half a no-op: the row flipped to `Cancelled` while the other host's engine kept executing — and paying for — every remaining block. The check costs one keyed read per block against work measured in seconds.

Tripping the fence cancels the run-scoped token, so every in-flight block's own token cancels with it. What the agent runtime does with a run whose block was abandoned that way is the [agent runtime integration](../services/infrastructure/agent-runtime-integration.md)'s concern, not the scheduler's.

`Undefined` counts as an **active** state throughout — in `WorkflowExecution.IsActive`, in its SQL-translatable twin `WorkflowExecutionExpressions.IsInActiveState`, and in the boundary check. `Undefined` is the creation default when no explicit initial state is passed — every production caller passes one today, but existing rows hold it — and both guard forms previously excluded it, so such a row could never be marked terminal at all: not by the guarded fail path, not by a user cancelling it. There is one home for the set — `WorkflowExecution.IsActiveState(WorkflowState)`, which `IsActive` delegates to and which the boundary check calls directly — plus the expression twin, which cannot call a method and must be kept in step by hand.

Widening that set is a semantic change for every consumer, not just the writers: `UpdateWorkflowExecutionStateCommandHandler` gates cancellability on the same guard, so an `Undefined` row is now cancellable exactly as a `Queued` one is.

Test-guarded by `platform/IniaAi.Core.Tests/Workflows/TerminalStateAbsorptionTests.cs` (the conditional writes, including the `Draft`-over-`Cancelled` race) and `platform/IniaAi.Core.Tests/Workflows/Scheduling/ExecutionFenceTests.cs` (the boundary check and the write-nothing rule).

## Cyclic-reference detection and breaking

Placeholders can form a cycle (mutual forward references). The layering loop detects a cycle when no node is ready but nodes remain, and breaks it deterministically at the **earliest-authored node of a cycle that nothing else still blocks**, reproducing the sequential `foreach`, where a forward placeholder simply resolved to no-data.

`ForceCycleNode` finds the strongly connected components among the blocked nodes and picks only from a cycle that no other blocked node feeds. A blocked node downstream of a cycle is never forced: forcing it would run it with no-data and leave the cycle in place. One consequence is that adding an edge that closes no cycle never changes which nodes are forced.

Broken nodes are reported via `SchedulingView.ForcedCycleNodes`, and `WorkflowHandler.BuildSchedulingView` logs them as a warning:

```csharp
if (view.ForcedCycleNodes.Count > 0)
{
    foreach (IBlockGroupStrategy group in GroupStrategies())
    {
        group.ThrowIfCycleBreakTouchesGroup(graph, view.ForcedCycleNodes);
    }

    Logger.LogWarning(
        "Workflow graph contains cyclic references; broke the cycle at: {Nodes}. The forward placeholders resolve to no-data.",
        string.Join(", ", view.ForcedCycleNodes));
}
```

A warning is the right answer unless a group strategy refuses the break: each registered
`IBlockGroupStrategy` is asked first, through `ThrowIfCycleBreakTouchesGroup`.


## Verification groups hold their shape

A build without the Verification module registers no group strategy: the handler runs a
verification group's members as ordinary blocks and holds none of their report output back, and the
stub verifier fails at once. `WorkflowGraphBuilder` adds no co-producer edges there, so a reader of
a member's tag waits only for that member.


## Parameterized-section expansion

Parameterized sections are **expanded before** the graph is built. `WorkflowHandler.ProcessParameterizedSectionsAsync` runs early in the whole-workflow path (right after variable processing, before signature-override preload). For each section carrying a `ParameterizedConfig`, it deserializes the config, calls `ParameterizedSectionExpander.Expand` to produce variant sections, replaces the template section in the workflow tree, and distributes documents to the variant blocks:

```csharp
List<Section> expandedSections = ParameterizedSectionExpander.Expand(templateSection, config);
IReadOnlyList<Document> allDocs = ParameterizedSectionExpander.CollectDocumentsFromTree(workflow);
workflow.ReplaceSectionWithExpanded(templateSection, expandedSections);
ParameterizedSectionExpander.DistributeDocumentsToVariants(expandedSections, allDocs);
```

Because expansion happens upstream, the graph builder never emits a `PARAMETERIZED_SECTION` node in production — it sees only the concrete variant blocks. The `PARAMETERIZED_SECTION` branches in `SchedulingViewBuilder.IsSchedulable` / `IsBarrier` are defensive cases reachable only from test fixtures that construct such a node directly. (Loop sections, by contrast, are expanded at runtime as their own barrier segment rather than pre-expanded.)

## Signature-override injection during execution

Per-organization signature overrides live in the Core `SignatureFieldOverride` table, one row per `(OrganizationId, WorkflowTypeId, PlaceholderName)`, and are injected into block inputs at execution time, with no DB round trip per block. The field definitions are workflow-level (`Workflow.SignatureFields`), so an override applies to every block that uses the field; no block id is part of the key. What the fields are and how an organization overrides them: [Signature Overrides](../../features/signature-overrides.md).

`WorkflowHandler.LoadActiveSignatureOverridesAsync` runs once per workflow execution and loads the organization's override rows for the workflow's type into a dictionary keyed by field name, ignoring case:

```csharp
private IReadOnlyDictionary<string, string>? _activeSignatureOverrides;
```

A workflow with no type or no signature fields gets an empty dictionary without a query. With no organization identity yet the dictionary stays null, and the single-section and single-block paths load it again once the identity is known.

During block execution, `InjectSignatureFieldValues` handles a block whose `Properties` implement `IHasSignatureFields`. For every workflow-level definition whose `{Name}` token appears in the block's templates (`InjectSignatureFieldValuesForBlock`), it writes the organization's override when there is one, otherwise the definition's default value, into the block's resolved inputs. On an AI Query block it also records the referenced field names in `CandidateSignatureFieldNames`, so replay capture knows which inputs were signature values:

```csharp
foreach (WorkflowSignatureField definition in definitions)
{
    if (string.IsNullOrWhiteSpace(definition.Name)
        || !properties.HasPlaceholder(
            Workflow.SignatureFieldToken(definition.Name), StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    inputs[definition.Name] = activeOverrides is not null
        && activeOverrides.TryGetValue(definition.Name, out string? overrideValue)
            ? overrideValue
            : definition.DefaultValue;
}
```

Because the key is the field name, loop clones and parameterized variants resolve overrides exactly as their template block does.

## Related pages

- [Execution Context](execution-context.md) — the `WorkflowHandler` lifecycle that drives the scheduler.
- [Loop Processing](loop-processing.md) — how `LoopStart`/`LoopEnd` sections expand and run as barrier segments.
- [Block Strategies](block-strategy-pattern.md) — the per-block-type execution logic invoked by the runner.
- [Workflow Engine Architecture](index.md) — the section/block hierarchy the DAG is built over.
