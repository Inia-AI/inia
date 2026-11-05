using IniaAi.Common;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Leaf-level node in the report tree. The per-leaf shape (Paragraph,
/// DynamicChart, Divider, Table) is carried by the <see cref="Kind"/>
/// discriminator rather than a distinct entity per kind.
/// </summary>
public class ReportParagraph : Entity<ReportParagraph>
{
    [Required]
    [StringLength(450)]
    [ForeignKey(nameof(Section))]
    public string SectionId { get; set; } = string.Empty;

    [Required]
    public ReportSection Section { get; set; } = default!;

    [StringLength(450)]
    public string? BlockId { get; set; }

    [ForeignKey(nameof(BlockId))]
    public Block? Block { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReportParagraphKind Kind { get; set; } = ReportParagraphKind.Paragraph;

    public int Order { get; set; } = 0;
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public double? Confidence { get; set; }
    public string? Content
    {
        get;
        set => field = value.RemoveNullCharacters();
    }
    public string? ReplacementTag { get; set; }

    public List<string> Sources { get; set; } = [];
    public List<Citation> Citations { get; set; } = [];

    /// <summary>
    /// For each block that a block-only citation in <see cref="Citations"/> reaches, every page
    /// and link that block or its upstream blocks cited. Stored once per block, because many
    /// citations can point to one block and the list can run to dozens of links.
    /// </summary>
    public List<BlockCitedSources> CitedSourcesByBlock { get; set; } = [];

    /// <summary>
    /// Chart payload, populated when <see cref="Kind"/> is
    /// <see cref="ReportParagraphKind.DynamicChart"/>. A single payload, not a
    /// collection: a leaf row carries at most one <c>ChartOutput</c>.
    /// </summary>
    public ChartOutput? ChartPayload { get; set; }

    /// <summary>
    /// Table payload, populated when <see cref="Kind"/> is
    /// <see cref="ReportParagraphKind.Table"/>. A single payload, like
    /// <see cref="ChartPayload"/>: a leaf row carries at most one <c>TableOutput</c>.
    /// </summary>
    public TableOutput? TablePayload { get; set; }

    public List<WorkflowExecutionLog>? WorkflowExecutionLogs { get; set; }
}
