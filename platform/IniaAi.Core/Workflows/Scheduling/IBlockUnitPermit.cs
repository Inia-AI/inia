namespace IniaAi.Core.Workflows.Scheduling;

/// <summary>
/// Brackets one permit from the workflow execution's <see cref="IBlockConcurrencyCap"/> around one
/// billable unit of work - today one block execution, later one verification leg. The permit belongs
/// to the unit, not to the scheduler node that dispatched it, so a node that runs units of its own
/// never waits on a permit it is already holding.
/// </summary>
/// <remarks>
/// <para>
/// For a block that takes a permit, the permitted region is the whole of
/// <c>WorkflowHandler.RunResolvedBlockUnitAsync</c> - the same region
/// <see cref="LevelScheduler"/>'s node guard held before the permit moved, so no bound on the
/// ordinary path changed in either direction. The two paragraphs below are about that region and
/// that region only.
/// </para>
/// <para>
/// <b>The fence probe stays inside on purpose.</b> It opens a service scope and a UserData database
/// connection at every block boundary, and it fails open: a read failure is logged as a warning and
/// the block runs anyway. Outside the permit, every block of a layer would probe at one instant, and
/// a layer's width is bounded by nothing - a loop section expands to one block per line of a
/// resolved template. This deployment configures no <c>Maximum Pool Size</c> and no pooled context
/// behind <c>IUserDataUnitOfWork</c>, so the effective ceiling is Npgsql's default of 100
/// connections per connection string, shared with every other consumer in the process. A wide layer
/// would exhaust it, every probe would time out after the 15-second default, every timeout would be
/// swallowed by the fail-open catch, and the run would keep spending with no fence at all.
/// </para>
/// <para>
/// <b>The failure-path persist stays inside too.</b> For a block that aborts the run it writes on
/// <see cref="CancellationToken.None"/>, so one permit can be pinned across an uncancellable write
/// against a sick database. That is today's behaviour unchanged, and the alternative - a whole
/// failing layer issuing uncapped uncancellable writes while the run tears down - is worse.
/// </para>
/// <para>
/// <b>A nested-dispatch node gives both of those bounds up, deliberately.</b>
/// <see cref="RunNestedDispatchAsync"/> takes no permit and wraps the node's whole shell, so its
/// fence probe, its per-block service scope and its failure-path persist all run uncapped - the
/// very fan-out the two paragraphs above refuse for ordinary blocks. The trade is forced: a node
/// cannot hold a permit while awaiting units that need one. What stays bounded is the part that
/// spends money, because every leg is a unit. What is unbounded is one fence read and one child
/// scope per concurrently-orchestrating node, and the count of those is bounded only by how many
/// loop-bearing blocks a template puts in one layer. The one shipped strategy answering
/// <c>DispatchesNestedUnits</c> true is the verification block with declared members. No gate
/// caps how many verified groups share a layer — the count is accepted as small because each
/// group must declare a verifier and members of its own, and the loop wall-clock gate refuses
/// templates whose groups cannot fit the janitor window.
/// </para>
/// <para>
/// <b>Lock order is permit-outermost.</b> A unit may take the per-counterparty write gate, the
/// chat-connector throttle or the agent poll gate inside its permit, and must release them before
/// the unit returns. Taking one of those first and then starting a unit inverts the order; the
/// counterparty gate is process-wide and keyed by data, so the resulting cycle would cross workflow
/// executions.
/// </para>
/// <para>
/// <b>Never resolve this from a block's service provider.</b> A block's
/// <c>BlockExecutionContext</c> is built from a child scope whose scoped
/// <see cref="IBlockConcurrencyCap"/> is a second semaphore, so resolving there would silently
/// unbind the cap. This type is deliberately not registered in DI, so that mistake does not
/// compile into a working call - the one instance a run may use is
/// <see cref="LevelScheduler.UnitPermits"/>, handed to the context explicitly.
/// </para>
/// </remarks>
public interface IBlockUnitPermit
{
    /// <summary>
    /// Runs <paramref name="unit"/> holding exactly one permit for its whole duration. A wait
    /// cancelled before the permit is granted takes none and releases none; every other outcome
    /// releases exactly one. The wait runs under <paramref name="cancellationToken"/>, so a caller
    /// that gives its unit a deadline covers the admission wait with it. Starting a unit from
    /// inside a unit throws rather than waiting: that call would be waiting on a permit its own
    /// flow already holds.
    /// </summary>
    Task RunUnitAsync(Func<CancellationToken, Task> unit, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the orchestration of a node that dispatches units of its own. Takes no permit, so the
    /// units it starts never wait on a permit their own node holds. The orchestration itself must
    /// stay non-billable: every model call, agent run and leg belongs inside a unit.
    ///
    /// A leg that goes through <c>WorkflowHandler.RunResolvedBlockAsync</c> is already bracketed by
    /// it and must not be wrapped in <see cref="RunUnitAsync"/> as well. The double bracket throws:
    /// against an ordinary block it lands on that block's error row, but against another
    /// nested-dispatch block it throws before any shell catch exists and ends the run as lost
    /// results instead of naming the wiring error.
    /// </summary>
    Task RunNestedDispatchAsync(Func<CancellationToken, Task> dispatch, CancellationToken cancellationToken);
}

/// <summary>
/// Default <see cref="IBlockUnitPermit"/> over one workflow execution's cap. This is the engine's
/// only <see cref="IBlockConcurrencyCap.AcquireAsync"/> call site; the paired release is in its
/// <c>finally</c>. Holds no disposable state, so it is safe on the workflow editor's pages, which
/// keep a handler alive past the scope that built it.
/// </summary>
internal sealed class BlockUnitPermit : IBlockUnitPermit
{
    private const string NESTED_UNIT_MESSAGE =
        "A unit of work cannot be started from inside another unit: it would wait on a permit its "
        + "own flow already holds, and at MaxBlockParallelism 1 it would never be granted. A block "
        + "that runs units of its own answers IBlockStrategy.DispatchesNestedUnits, so its "
        + "orchestration runs with no permit and each unit takes one of its own.";

    /// <summary>
    /// The permit a <c>BlockExecutionContext</c> carries when no engine run owns it - a
    /// strategy-level unit test, or any caller that builds a context by hand. Uncapped, so a
    /// strategy needs no null check; it still refuses a nested unit, so a strategy cannot pass off
    /// the engine and throw on it.
    /// </summary>
    public static readonly IBlockUnitPermit Unbounded = new BlockUnitPermit(new UncappedCap());

    private readonly IBlockConcurrencyCap _cap;
    private readonly AsyncLocal<bool> _insideUnit = new();

    public BlockUnitPermit(IBlockConcurrencyCap cap)
    {
        _cap = cap;
    }

    public async Task RunUnitAsync(Func<CancellationToken, Task> unit, CancellationToken cancellationToken)
    {
        ThrowIfInsideUnit();

        // Outside the try on purpose: a wait cancelled before the permit is granted must release
        // nothing.
        await _cap.AcquireAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // The runtime restores the caller's ExecutionContext when this method returns, so the
            // flag reaches the unit and everything it awaits without ever escaping upward.
            _insideUnit.Value = true;

            await unit(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cap.Release();
        }
    }

    public async Task RunNestedDispatchAsync(
        Func<CancellationToken, Task> dispatch,
        CancellationToken cancellationToken)
    {
        ThrowIfInsideUnit();

        await dispatch(cancellationToken).ConfigureAwait(false);
    }

    private void ThrowIfInsideUnit()
    {
        if (_insideUnit.Value)
        {
            throw new InvalidOperationException(NESTED_UNIT_MESSAGE);
        }
    }

    private sealed class UncappedCap : IBlockConcurrencyCap
    {
        public int MaxParallelism => int.MaxValue;

        public Task AcquireAsync(CancellationToken ct) => Task.CompletedTask;

        public void Release()
        {
        }
    }
}
