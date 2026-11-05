using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Scheduling;

/// <summary>
/// Executes a <see cref="SchedulingView"/> layer by layer, dispatching every block in a layer at
/// once. The concurrency cap is not applied here: it binds one layer lower, around each billable
/// unit of work, through <see cref="UnitPermits"/>. Persistence is not the scheduler's concern:
/// each block writes its own result through its own pooled context.
/// </summary>
public sealed class LevelScheduler
{
    private readonly IBlockConcurrencyCap _cap;
    private readonly ILogger<LevelScheduler> _logger;

    public LevelScheduler(IBlockConcurrencyCap cap, ILogger<LevelScheduler> logger)
    {
        _cap = cap;
        _logger = logger;
        UnitPermits = new BlockUnitPermit(cap);
    }

    /// <summary>
    /// The run's permit bracket. The scheduler no longer takes a permit per node - a node that runs
    /// units of its own would otherwise wait on a permit it holds - but it owns the instance, so
    /// every unit of one workflow execution brackets against the same scoped cap without a new
    /// constructor parameter on the handler and its subclasses. Built from the scheduler's own cap,
    /// so a mis-wired permit is impossible.
    /// </summary>
    public IBlockUnitPermit UnitPermits { get; }

    /// <summary>
    /// Runs every block in <paramref name="view"/> via <paramref name="runBlock"/>, respecting
    /// layer ordering. The per-workflow (scoped) concurrency cap is honoured by
    /// <paramref name="runBlock"/> itself, which brackets each unit of work in a permit from
    /// <see cref="UnitPermits"/>. Block-level exceptions are expected to be caught inside
    /// <paramref name="runBlock"/> (catch-log-continue); the scheduler's outer catch is
    /// belt-and-braces for runner-shell exceptions. Cancellation propagates.
    /// </summary>
    /// <returns>
    /// The number of non-cancellation exceptions that escaped <paramref name="runBlock"/>. Each one
    /// is a block whose failure handling itself failed, so its persisted state cannot be trusted;
    /// callers surface a non-zero count as lost results rather than a completed run.
    /// </returns>
    public async Task<int> RunAsync(
        SchedulingView view,
        Func<string, CancellationToken, Task> runBlock,
        CancellationToken ct)
    {
        // DOP=1 fast path: dispatch in SchedulingView.SequentialOrder. The order is a priority-
        // aware topological emission keyed by (SectionOrder, BlockOrder) computed at build time,
        // so it matches today's sequential foreach over workflow.Sections then section.Blocks.
        // Layered Kahn dispatch (cap >= 2) groups independent blocks across sections so they can
        // overlap; at cap=1 that grouping is observable only as a strict execution order, and
        // the layered order does not match sequential foreach for parameterized / loop workflows
        // where independent ready blocks span sections. Bypass it at cap=1 to honour the DOP=1
        // equivalence contract documented on top-level contract #1 of the plan.
        if (_cap.MaxParallelism == 1)
        {
            return await RunSequentialAsync(view, runBlock, ct).ConfigureAwait(false);
        }

        int escapedFailures = 0;

        foreach (SchedulingLayer layer in view.Layers)
        {
            ct.ThrowIfCancellationRequested();

            Task[] tasks = [.. layer.BlockIds
                .Select(id => RunNodeAsync(id, runBlock, ct))];

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // WhenAll under mixed OCE + non-OCE may propagate the non-OCE first; if any
                // task is canceled, surface that as the layer outcome so cancellation is never
                // masked by a sibling's regular exception.
                Task? canceledTask = tasks.FirstOrDefault(t => t.IsCanceled);
                if (canceledTask is not null)
                {
                    canceledTask.GetAwaiter().GetResult();
                }

                // Belt-and-braces: block-level failures should already have been logged by
                // runBlock (catch-log-continue). This branch surfaces anything that escaped.
                foreach (Task faulted in tasks.Where(t => t.IsFaulted))
                {
                    escapedFailures++;
                    _logger.LogError(faulted.Exception, "Unhandled exception escaped the block runner in a parallel layer");
                }
            }
        }

        return escapedFailures;
    }

    private static async Task RunNodeAsync(
        string blockId,
        Func<string, CancellationToken, Task> runBlock,
        CancellationToken ct)
    {
        // The layer's Task[] is materialised before the try that guards it, so a delegate throwing
        // synchronously would escape uncounted. Awaiting it here turns that into a faulted task.
        await runBlock(blockId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// cap=1 sequential dispatch. Runs every block in <see cref="SchedulingView.SequentialOrder"/>
    /// one at a time. Serialisation comes from the awaited foreach; the cap is honoured per unit of
    /// work by <paramref name="runBlock"/>, and at cap=1 that one-permit semaphore is still the
    /// source of truth on the concurrency floor for any unit a block dispatches for itself.
    /// Block-level failures are caught inside <paramref name="runBlock"/>; we let any escapee
    /// bubble so the caller sees the same exception surface as the layered path.
    /// </summary>
    private async Task<int> RunSequentialAsync(
        SchedulingView view,
        Func<string, CancellationToken, Task> runBlock,
        CancellationToken ct)
    {
        int escapedFailures = 0;

        foreach (string blockId in view.SequentialOrder)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await runBlock(blockId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // runBlock should already catch-log-continue; an exception here escaped its catch.
                escapedFailures++;
                _logger.LogError(ex, "Unhandled exception escaped the block runner during sequential dispatch");
            }
        }

        return escapedFailures;
    }
}
