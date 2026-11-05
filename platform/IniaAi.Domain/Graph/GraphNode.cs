using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Graph;

/// <summary>
/// Entity representing a node in the workflow execution graph.
/// Maps to the 'graph_nodes' table in Postgres.
/// </summary>
public class GraphNode
{
    // Composite key part 1: which workflow execution run this node belongs to
    [Required]
    [MaxLength(450)]
    public required string RunId { get; set; }

    // Composite key part 2: unique identifier within this run (ReplacementTag or Block.Id)
    [Required]
    [MaxLength(450)]
    public required string Key { get; set; }

    // Type of node: "block", "input_variable", "unknown", or "document"
    [Required]
    [MaxLength(50)]
    public required string Kind { get; set; }

    // Display name (block name, variable tag, document name)
    [MaxLength(500)]
    public string? Label { get; set; }

    // Execution order for blocks (null for variables/documents)
    public int? Order { get; set; }

    // Document class filter (e.g., "SEC Filing; Prospectus")
    [MaxLength(500)]
    public string? DocumentClass { get; set; }

    // For block nodes: the block's ReplacementTag. Nodes are keyed (Key) by Block.Id so two blocks
    // sharing a tag stay distinct, so the tag is persisted separately for the provenance query path,
    // which addresses nodes by ReplacementTag. Null for variable/document/unknown nodes.
    [MaxLength(450)]
    public string? ReplacementTag { get; set; }

    // Note: EF Core will recognize RunId + Key as composite primary key
    // when configured in the EntityTypeConfiguration
}
