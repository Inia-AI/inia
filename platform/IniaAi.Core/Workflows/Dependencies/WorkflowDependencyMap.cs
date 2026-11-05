using IniaAi.Core.Workflows.Graph;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Dependencies;

/// <summary>
/// The one dependency rule of a workflow: which block reads which block's output, documents or
/// counterparty write-back, and which inputs it reads. The scheduler orders blocks from it
/// (<see cref="ToSchedulingGraph"/>) and the editor draws it (the public members), so what the
/// editor shows is what the run waits for.
/// </summary>
public sealed class WorkflowDependencyMap
{
    private readonly Dictionary<string, DependencyBlock> _blocksById;
    private readonly Dictionary<string, DependencyInput> _inputsById;
    private readonly ILookup<string, DependencyEdge> _edgesBySource;
    private readonly ILookup<string, DependencyEdge> _edgesByTarget;
    private readonly IReadOnlyList<DependencyTagRead> _tagReads;

    internal WorkflowDependencyMap(
        IReadOnlyList<DependencyBlock> blocks,
        IReadOnlyList<DependencyInput> inputs,
        IReadOnlyList<DependencyEdge> edges,
        IReadOnlyList<DependencyVerificationGroup> verificationGroups,
        IReadOnlyList<DependencyIssue> issues,
        IReadOnlyList<DependencyTagRead> tagReads)
    {
        Blocks = blocks;
        Inputs = inputs;
        Edges = edges;
        VerificationGroups = verificationGroups;
        Issues = issues;
        _tagReads = tagReads;
        _blocksById = blocks.ToDictionary(static block => block.Id, StringComparer.Ordinal);
        _inputsById = inputs.ToDictionary(static input => input.Id, StringComparer.Ordinal);
        _edgesBySource = edges.ToLookup(static edge => edge.SourceId, StringComparer.Ordinal);
        _edgesByTarget = edges.ToLookup(static edge => edge.TargetId, StringComparer.Ordinal);
    }

    /// <summary>Every block in authored order (section order, then block order).</summary>
    public IReadOnlyList<DependencyBlock> Blocks { get; }

    /// <summary>Every workflow variable, then every other input some block or section gate reads.</summary>
    public IReadOnlyList<DependencyInput> Inputs { get; }

    /// <summary>
    /// Data, document, counterparty and gate edges. Execution-order fences and the verifier's
    /// co-producer edges are scheduling mechanics, not data flow, and never appear here.
    /// </summary>
    public IReadOnlyList<DependencyEdge> Edges { get; }

    public IReadOnlyList<DependencyVerificationGroup> VerificationGroups { get; }

    /// <summary>Unresolved tags and cycles. Unread blocks are not issues.</summary>
    public IReadOnlyList<DependencyIssue> Issues { get; }

    /// <summary>
    /// The map of a template workflow as the editor holds it, unsaved edits included. A
    /// parameterized section is scanned once per variant with that variant's rewrite applied in
    /// memory, and every variant folds back onto its template block, so the map keys blocks by
    /// template block id. Section gates contribute <see cref="DependencyEdgeKind.Gate"/> edges.
    /// </summary>
    public static WorkflowDependencyMap ForTemplate(Workflow workflow)
    {
        return DependencyMapBuilder.ForTemplate(workflow);
    }

    /// <summary>
    /// The map of one dispatch batch: blocks already expanded, keyed by their section order.
    /// Gates are left out because the dispatcher orders gated sections by segment.
    /// </summary>
    public static WorkflowDependencyMap ForDispatch(
        IReadOnlyDictionary<int, List<Block>> blocksBySectionOrder,
        IReadOnlyList<WorkflowVariable> variables)
    {
        return DependencyMapBuilder.ForDispatch(blocksBySectionOrder, variables);
    }

    public DependencyBlock? FindBlock(string blockId)
    {
        return _blocksById.GetValueOrDefault(blockId);
    }

    public DependencyInput? FindInput(string inputId)
    {
        return _inputsById.GetValueOrDefault(inputId);
    }

    /// <summary>The edges this block or input feeds.</summary>
    public IEnumerable<DependencyEdge> OutgoingEdges(string nodeId)
    {
        return _edgesBySource[nodeId];
    }

    /// <summary>The edges feeding this block.</summary>
    public IEnumerable<DependencyEdge> IncomingEdges(string nodeId)
    {
        return _edgesByTarget[nodeId];
    }

    /// <summary>
    /// The graph the scheduler layers. Data edges as they are; where verification groups run,
    /// each verifier becomes a co-producer of its members' tags so readers wait for the verdict;
    /// fence blocks are pinned at their authored position; document and counterparty edges are
    /// added only where they close no cycle, so a workflow that scheduled without a forced cycle
    /// break still does.
    /// Gate edges are left out.
    /// </summary>
    /// <param name="fenceBlockIds">
    /// Blocks whose handler side effects (SharedInputs flags, CounterpartyId, AllPagesTopics) are
    /// invisible to placeholder analysis. Each is pinned at its authored position: edges from every
    /// earlier-authored block into it, and from it to every later-authored block.
    /// </param>
    /// <param name="verificationGroupsRun">
    /// False in a build without the Verification module. Its verifier is a stub that runs no
    /// loop, so a member's readers wait only for the member.
    /// </param>
    public WorkflowGraph ToSchedulingGraph(IReadOnlySet<string>? fenceBlockIds = null, bool verificationGroupsRun = true)
    {
        return SchedulingGraphProjection.Project(this, _tagReads, fenceBlockIds, verificationGroupsRun);
    }
}

/// <summary>A read resolved through a block's replacement tag, kept for the verifier co-producer edges.</summary>
internal sealed record DependencyTagRead(string ReaderId, string BaseTag, string Placeholder);
