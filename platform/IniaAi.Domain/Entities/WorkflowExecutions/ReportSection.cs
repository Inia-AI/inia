using IniaAi.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Section-level node in the report tree. Parented by a
/// <see cref="WorkflowReport"/>, owns leaf <see cref="ReportParagraph"/> rows.
/// </summary>
public class ReportSection : Entity<ReportSection>
{
    [Required]
    [StringLength(450)]
    [ForeignKey(nameof(WorkflowReport))]
    public string WorkflowReportId { get; set; } = string.Empty;

    [Required]
    public WorkflowReport WorkflowReport { get; set; } = default!;

    public int Order { get; set; } = 0;
    public string? Title { get; set; }

    /// <summary>
    /// Grid layout snapshot captured at execution time. A single payload, not a
    /// collection: a section carries at most one <c>GridLayoutOutput</c>.
    /// </summary>
    public GridLayoutOutput? Layout { get; set; }

    /// <summary>
    /// When true, the Word and Markdown exports leave this section out; the app report still shows it.
    /// Snapshot of the workflow section's setting at execution time.
    /// </summary>
    public bool ExcludeFromWordExport { get; set; }

    [InverseProperty(nameof(ReportParagraph.Section))]
    public ICollection<ReportParagraph> Paragraphs { get; private set; } = [];

    public void Add(ReportParagraph paragraph)
    {
        paragraph.Order = Paragraphs.Count;
        paragraph.Section = this;
        paragraph.SectionId = Id;
        Paragraphs.Add(paragraph);
    }

    public void Remove(ReportParagraph paragraph)
    {
        if (!Paragraphs.Remove(paragraph))
        {
            return;
        }

        var ordered = Paragraphs.OrderBy(p => p.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }
    }

    public void Clear()
    {
        Paragraphs.Clear();
    }

    public ReportParagraph? FindParagraphByReplacementTag(string replacementTag)
    {
        return Paragraphs.FirstOrDefault(p => p.ReplacementTag == replacementTag);
    }
}
