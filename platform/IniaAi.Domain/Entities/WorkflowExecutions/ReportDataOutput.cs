using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// One block's JSON that a run hands to other systems beside its report, as the block produced it,
/// with the citations of the figures inside it. Never edited: the report is what analysts edit.
/// </summary>
[Index(nameof(WorkflowReportId), nameof(Name))]
public class ReportDataOutput : Entity<ReportDataOutput>
{
    [Required]
    [StringLength(450)]
    [ForeignKey(nameof(WorkflowReport))]
    public string WorkflowReportId { get; set; } = string.Empty;

    [Required]
    public WorkflowReport WorkflowReport { get; set; } = default!;

    [StringLength(450)]
    public string? BlockId { get; set; }

    [ForeignKey(nameof(BlockId))]
    public Block? Block { get; set; }

    [Required]
    [StringLength(Block.MAX_REPLACEMENT_TAG_LENGTH)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The JSON exactly as the block produced it. Stored as text, not as a JSON column type, which
    /// would reorder its keys.
    /// </summary>
    [Required]
    public string Json { get; set; } = string.Empty;

    public List<DataOutputCitation> Citations { get; set; } = [];

    /// <summary>
    /// For each block that a block-only citation reaches, every page and link that block or its
    /// upstream blocks cited, as on a report paragraph.
    /// </summary>
    public List<BlockCitedSources> CitedSourcesByBlock { get; set; } = [];
}
