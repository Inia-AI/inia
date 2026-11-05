using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Interfaces;

/// <summary>
/// A strategy whose block runs a group of other blocks of the same dispatch: the blocks that carry
/// the tags <see cref="MemberTagsOf"/> names. The engine runs each member with its report output
/// held back. When the group block's turn comes and it
/// <see cref="IBlockStrategy.DispatchesNestedUnits"/>, the engine hands the whole group to
/// <see cref="RunGroupAsync"/>, which releases the members' output through
/// <see cref="IBlockGroupHost.ReleaseWithheldMembersAsync"/>. Without such a strategy for the
/// group block's type, the members run as ordinary blocks.
/// </summary>
internal interface IBlockGroupStrategy : IBlockStrategy
{
    IReadOnlyCollection<string> MemberTagsOf(Block groupBlock);

    /// <summary>Refuses, before anything spends, dispatch segments that cannot run a group whole.</summary>
    void ThrowIfSegmentsCannotHoldGroups(IReadOnlyList<DispatchSegment> segments);

    /// <summary>Refuses a schedule whose cycle break landed on a group.</summary>
    void ThrowIfCycleBreakTouchesGroup(WorkflowGraph graph, IReadOnlyList<string> forcedCycleNodes);

    Task<string?> RunGroupAsync(BlockGroupRun run, CancellationToken cancellationToken);

    /// <summary>The warning a finished run carries for its groups, or null when it needs none.</summary>
    (string Kind, string Detail)? CompletionWarningFor(Workflow workflow);

    /// <summary>The group block's own log row.</summary>
    /// <param name="modelTiers">The executing organization's agent model tiers, which name the model the row reports.</param>
    WorkflowExecutionLog BuildLog(Section section, Block block, string workflowExecutionId, AgentModelTiers modelTiers);
}
