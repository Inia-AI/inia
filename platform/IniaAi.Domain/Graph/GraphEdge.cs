using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Graph;

/// <summary>
/// Entity representing an edge between nodes in the workflow execution graph.
/// Maps to the 'graph_edges' table in Postgres.
/// </summary>

public class GraphEdge
{
    // Composite key part 1: which workflow execution run this node belongs to
    [Required]
    [MaxLength(450)]
    public required string RunId { get; set; }

    // Composite key part 2: unique identifier for the source node
    [Required]
    [MaxLength(450)]
    public required string SourceId { get; set; }

    // Composite key part 3: unique identifier for the target node
    [Required]
    [MaxLength(450)]
    public required string TargetId { get; set; }

    // The exact placeholder reference (e.g., "InputContext.answer", "BlockX.markdown")
    [MaxLength(500)]
    public string? Label { get; set; }
}
