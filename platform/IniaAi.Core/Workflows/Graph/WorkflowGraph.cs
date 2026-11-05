using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Graph;

public class WorkflowGraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Label { get; set; }
    public int? Order { get; set; }

    /// <summary>
    /// For block nodes, the parent section's order key. Null for variable and unknown nodes.
    /// Used by <c>SchedulingViewBuilder</c> to tie-break blocks in the same layer by
    /// <c>(SectionOrder, Order)</c>, matching today's sequential foreach ordering.
    /// </summary>
    public int? SectionOrder { get; set; }

    /// <summary>
    /// For block nodes, the <see cref="BlockTypes"/> enum value. Null for variable and unknown
    /// nodes. Used by <c>SchedulingViewBuilder.IsBarrier</c> to identify doc-producing blocks
    /// and other block-type-keyed scheduling rules.
    /// </summary>
    public BlockTypes? BlockType { get; set; }

    /// <summary>
    /// For block nodes, the block's <c>ReplacementTag</c> (when set). Block nodes are keyed by
    /// <c>block.Id</c> so two blocks sharing a tag stay distinct nodes; the tag is preserved here so
    /// the edge pass can resolve a reader's placeholder base-tag to every producer that supplies it.
    /// Null for variable, document-input and unknown nodes (those are keyed by their tag directly).
    /// </summary>
    public string? ReplacementTag { get; set; }

    public string? DocumentClass { get; set; }

    /// <summary>
    /// True when this block creates an output document (its properties select a
    /// <c>CreatesDocumentClass</c>). Such a block writes the shared <c>AllPages</c> list during
    /// execution, so the scheduler treats it as a barrier (its own layer) to avoid concurrent
    /// document-producers racing on that list.
    /// </summary>
    public bool ProducesDocument { get; set; }

    /// <summary>
    /// True when the block's author flagged it to run alone (<c>Block.RunsAlone</c>). The scheduler
    /// treats it as a barrier (its own layer), independent of document production.
    /// </summary>
    public bool ManualBarrier { get; set; }

    /// <summary>
    /// The verifier's tag (its id when untagged), stamped on the verifier and every member it
    /// names, so a forced cycle break landing on the group can be refused. Null outside a
    /// verification group.
    /// </summary>
    public string? VerificationGroup { get; set; }
}

public class WorkflowGraphEdge
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string? Label { get; set; }
}

public class WorkflowGraph
{
    public List<WorkflowGraphNode> Nodes { get; set; } = [];
    public List<WorkflowGraphEdge> Edges { get; set; } = [];
}
