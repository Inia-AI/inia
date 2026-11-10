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

    /// <summary>
    /// The layout of the paragraphs this section shows. A report leaves out a block that put nothing in it,
    /// and an analyst can delete a paragraph, so the stored layout can hold positions with nothing to place.
    /// Those positions go, and an element with nothing beside it in its rows takes the full width,
    /// whether the workflow placed it alone or its neighbour is missing.
    /// The stored layout stays as the workflow defined it.
    /// </summary>
    public GridLayoutOutput? ShownLayout()
    {
        if (Layout is null || Layout.Mode != GridLayoutOutput.GRID_MODE)
        {
            return Layout;
        }

        HashSet<int> shownOrders = [.. Paragraphs.Select(paragraph => paragraph.Order)];
        List<GridElementPosition> shown = [.. Layout.Elements.Where(position => shownOrders.Contains(position.BlockOrder))];

        return new GridLayoutOutput
        {
            Name = Layout.Name,
            Mode = Layout.Mode,
            Columns = Layout.Columns,
            Elements = [.. shown.Select(position => shown.Exists(other => other != position && SharesARow(position, other))
                ? position
                : new GridElementPosition
                {
                    BlockOrder = position.BlockOrder,
                    Row = position.Row,
                    RowSpan = position.RowSpan,
                    Column = 1,
                    ColumnSpan = Layout.Columns > 0 ? Layout.Columns : GridLayoutOutput.DEFAULT_COLUMNS,
                })],
        };
    }

    private static bool SharesARow(GridElementPosition a, GridElementPosition b)
    {
        return a.Row <= LastRow(b) && b.Row <= LastRow(a);
    }

    private static int LastRow(GridElementPosition position)
    {
        return position.Row + Math.Max(position.RowSpan, 1) - 1;
    }
}
