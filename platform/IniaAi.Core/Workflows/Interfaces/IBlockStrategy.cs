using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Interfaces;

/// <summary>
/// Runs one block type. A type outside the platform registers its strategy with
/// <c>AddBlockType</c>; the strategy reads its own services from <see cref="BlockExecutionContext.Services"/>,
/// the scope the block runs in. One instance runs every block of its type in every concurrent run,
/// so a strategy keeps no state of a run in its fields.
/// </summary>
public interface IBlockStrategy
{
    Task<BlockStrategyOutput> ExecuteBlockAsync(Block block, BlockExecutionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when this block runs billable units of its own - the verification loop's redo legs -
    /// through <see cref="BlockExecutionContext.UnitPermits"/>. The engine then dispatches the
    /// block's node with no concurrency permit and every unit takes one, because a node holding a
    /// permit while it awaits a unit that needs one waits on a permit it owns. Takes the block so a
    /// strategy can answer per configuration rather than per type: a verification block with no
    /// loop dispatches nothing and stays an ordinary permitted unit. The engine asks this before a
    /// block's shell exists, so an implementation must be a pure read and must not throw: an
    /// exception here faults the node task and the run reports lost results.
    /// </summary>
    bool DispatchesNestedUnits(Block block)
    {
        return false;
    }
}
