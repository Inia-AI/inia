using IniaAi.Common.Constants;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats.Word;

namespace IniaAi.FileFormats.Markdown;

/// <summary>
/// Writes a run's report as one Markdown document from the element tree the Word export reads, so
/// both carry the same sections in the same order. A chart becomes a table of its data.
/// </summary>
public sealed class MarkdownReportWriter : IReportFileWriter
{
    private const string EXTENSION = ".md";
    private const string CONTENT_TYPE = "text/markdown; charset=utf-8";
    private const int TITLE_HEADING_LEVEL = 1;
    private const int SECTION_HEADING_LEVEL = 2;
    private const string COUNTERPARTY_LABEL = "**" + CounterpartyTerms.SINGULAR + ":** ";
    private const string ANALYST_LABEL = "**Analyst:** ";

    public ExportFormat Format => ExportFormat.Markdown;

    public string Extension => EXTENSION;

    public string ContentType => CONTENT_TYPE;

    public Task<BinaryContent> WriteAsync(IDocumentElement document, ReportFileOptions options)
    {
        return Task.FromResult(new BinaryContent(BinaryData.FromString(Write(document, options.FooterNotice)), MediaType.TextMarkdown));
    }

    public static string Write(IDocumentElement root)
    {
        return Write(root, footerNotice: null);
    }

    private static string Write(IDocumentElement root, string? footerNotice)
    {
        var blocks = new List<string>();
        AddHeader(root, blocks);

        foreach (IDocumentElement child in root.Children)
        {
            AddElement(child, SECTION_HEADING_LEVEL, blocks);
        }

        if (!string.IsNullOrWhiteSpace(footerNotice))
        {
            blocks.Add(MarkdownText.HORIZONTAL_RULE);
            blocks.Add(MarkdownText.EscapeLineStart(MarkdownText.Escape(footerNotice)));
        }

        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        blocks[^1] += MarkdownText.LINE_BREAK;
        return string.Join(MarkdownText.BLOCK_SEPARATOR, blocks);
    }

    private static void AddHeader(IDocumentElement root, List<string> blocks)
    {
        AddHeading(root.Title, TITLE_HEADING_LEVEL, blocks);

        List<string> lines = [];
        AddLabelledLine(root.Metadata, DocumentMetadataKeys.COUNTERPARTY, COUNTERPARTY_LABEL, lines);
        AddLabelledLine(root.Metadata, DocumentMetadataKeys.ANALYST_NAME, ANALYST_LABEL, lines);
        if (lines.Count > 0)
        {
            blocks.Add(string.Join(MarkdownText.HARD_BREAK, lines));
        }
    }

    private static void AddLabelledLine(IDictionary<string, string?> metadata, string key, string label, List<string> lines)
    {
        if (metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}{MarkdownText.Escape(value.Trim())}");
        }
    }

    private static void AddElement(IDocumentElement element, int headingLevel, List<string> blocks)
    {
        switch (element.Type)
        {
            case DocumentElementType.Section:
                if (string.IsNullOrWhiteSpace(element.Content) && element.Children.Count == 0)
                {
                    return;
                }

                AddHeading(element.Title, headingLevel, blocks);
                if (element.Children.Count == 0)
                {
                    AddHeading(element.Subtitle, headingLevel + 1, blocks);
                }

                AddContent(element.Content, headingLevel, blocks);
                break;

            case DocumentElementType.Paragraph:
                AddHeading(element.Subtitle, headingLevel, blocks);
                AddContent(element.Content, headingLevel, blocks);
                break;

            case DocumentElementType.Divider:
                blocks.Add(MarkdownText.HORIZONTAL_RULE);
                break;

            case DocumentElementType.Table:
                AddContent(element.Content, headingLevel, blocks);
                break;

            case DocumentElementType.Chart:
                ChartOutput? chart = element.OutputData.OfType<ChartOutput>().FirstOrDefault();
                if (chart is not null)
                {
                    AddChart(chart, blocks);
                }
                else
                {
                    AddContent(element.Content, headingLevel, blocks);
                }

                break;
        }

        foreach (IDocumentElement child in ReadingOrder(element))
        {
            AddElement(child, headingLevel + 1, blocks);
        }
    }

    /// <summary>
    /// A grid section reads row by row and, in a row, column by column, as the Word export lays it
    /// out; the blocks the grid does not place follow in their own order.
    /// </summary>
    private static IEnumerable<IDocumentElement> ReadingOrder(IDocumentElement element)
    {
        GridLayoutOutput? grid = element.OutputData.OfType<GridLayoutOutput>().FirstOrDefault();
        if (grid is null || grid.Mode != GridLayoutOutput.GRID_MODE)
        {
            return element.Children;
        }

        List<IDocumentElement> placed = [.. grid.Elements
            .OrderBy(position => position.Row)
            .ThenBy(position => position.Column)
            .Select(position => element.Children.FirstOrDefault(child => child.Order == position.BlockOrder))
            .OfType<IDocumentElement>()
            .Distinct()];
        return placed.Concat(element.Children.Where(child => !placed.Contains(child)));
    }

    private static void AddHeading(string? text, int level, List<string> blocks)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            blocks.Add(MarkdownText.Heading(level, MarkdownText.Escape(text)));
        }
    }

    private static void AddContent(string? html, int headingOffset, List<string> blocks)
    {
        if (!string.IsNullOrWhiteSpace(html))
        {
            blocks.AddRange(HtmlToMarkdown.ToBlocks(html, headingOffset));
        }
    }

    private static void AddChart(ChartOutput chart, List<string> blocks)
    {
        ChartDataTable table = ChartDataTable.From(chart);
        blocks.Add(MarkdownText.Escape(table.Caption));

        if (table.Rows.Count == 0)
        {
            blocks.Add(ChartDataTable.NO_DATA_TEXT);
            return;
        }

        IEnumerable<IReadOnlyList<string>> rows = new[] { table.Header }.Concat(table.Rows);
        blocks.Add(MarkdownText.Table([.. rows.Select(row => (IReadOnlyList<string>)[.. row.Select(MarkdownText.Escape)])]));
    }
}
