using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;

namespace IniaAi.FileFormats.ContractTests.Reports;

/// <summary>Small invented reports, each built to show one rule of the Word export.</summary>
public static class SmallReportFixtures
{
    public const string PADDED_SECTION = "Padded Section";
    public const string PADDED_SUBTITLE = "Padded Subtitle";
    public const string PADDED_BODY = "Padded body content.";
    public const string GRID_LAST_ITEM = "item two";
    public const string SECTION_AFTER_GRID = "Leadership & Governance";
    public const string GRID_FIRST_COLUMN_TEXT = "Revenue grew in the first column.";
    public const string GRID_PAST_LAST_COLUMN_TEXT = "Margins held past the last column.";

    private const string FINANCIAL_SUMMARY_HTML = """
        <h2>Financial Summary</h2>
        <table border="1">
            <tr>
                <th colspan="2">Q4 2023 Results</th>
            </tr>
            <tr>
                <td style="font-weight: bold">Revenue</td>
                <td align="right">$1,234,567</td>
            </tr>
            <tr>
                <td style="font-weight: bold">Expenses</td>
                <td align="right">$987,654</td>
            </tr>
            <tr style="background-color: #f0f0f0">
                <td style="font-weight: bold">Net Income</td>
                <td align="right">$246,913</td>
            </tr>
        </table>
        """;

    private const string DETAILED_ANALYSIS_HTML = """
        <table>
            <tr>
                <th>Metric</th>
                <th>2022</th>
                <th>2023</th>
                <th>Change</th>
            </tr>
            <tr>
                <td>ROE</td>
                <td>15.2%</td>
                <td>16.8%</td>
                <td>+1.6%</td>
            </tr>
            <tr>
                <td>Profit Margin</td>
                <td>22.4%</td>
                <td>24.1%</td>
                <td>+1.7%</td>
            </tr>
        </table>
        """;

    /// <summary>A report with no title, no section and no analyst.</summary>
    public static WorkflowReport Minimal()
    {
        return new WorkflowReport();
    }

    /// <summary>HTML tables with a merged header, right-aligned cells and a shaded row, and text over several lines.</summary>
    public static WorkflowReport HtmlTables()
    {
        var report = new WorkflowReport { Title = "Complex Report", AnalystName = "John Doe", ReviewDate = new DateTime(2026, 6, 17) };
        var first = new ReportSection { Title = "Section 1" };
        report.Add(first);
        first.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = FINANCIAL_SUMMARY_HTML });
        var second = new ReportSection { Title = "Section 2" };
        report.Add(second);
        second.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = "Analysis content with\nmultiple\nlines" });
        second.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Title = "Detailed Analysis", Content = DETAILED_ANALYSIS_HTML });
        return report;
    }

    /// <summary>A Markdown heading, emphasis, a bullet list and a table in one paragraph.</summary>
    public static WorkflowReport MarkdownBasics()
    {
        return OneParagraph("# Section heading\n\nSome **bold** text and *italic* text.\n\n- bullet one\n- bullet two\n\n| Col A | Col B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |");
    }

    /// <summary>A title, a section title, a subtitle and a body with white space at both ends.</summary>
    public static WorkflowReport Padded()
    {
        var report = new WorkflowReport { Title = "  Padded Report  " };
        var section = new ReportSection { Title = "  " + PADDED_SECTION + "  " };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Subtitle = "  " + PADDED_SUBTITLE + "  ", Content = "  " + PADDED_BODY + "  " });
        return report;
    }

    /// <summary>One section that holds one paragraph of the given content.</summary>
    public static WorkflowReport OneParagraph(string content)
    {
        var report = new WorkflowReport { Title = "T" };
        var section = new ReportSection { Title = "Section" };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = content });
        return report;
    }

    /// <summary>
    /// A two-column grid whose second block sits in a third column, as a grid keeps its positions when an editor lowers
    /// its column count.
    /// </summary>
    public static WorkflowReport GridWithABlockPastItsLastColumn()
    {
        var report = new WorkflowReport { Title = "T" };
        var grid = new ReportSection
        {
            Title = "Market Data",
            Layout = new GridLayoutOutput
            {
                Mode = GridLayoutOutput.GRID_MODE,
                Columns = 2,
                Elements =
                [
                    new GridElementPosition { BlockOrder = 0, Row = 1, Column = 1, ColumnSpan = 1, RowSpan = 1 },
                    new GridElementPosition { BlockOrder = 1, Row = 1, Column = 3, ColumnSpan = 1, RowSpan = 1 },
                ],
            },
        };
        report.Add(grid);
        grid.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Order = 0, Content = GRID_FIRST_COLUMN_TEXT });
        grid.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Order = 1, Content = GRID_PAST_LAST_COLUMN_TEXT });
        return report;
    }

    /// <summary>A one-column grid section that ends in a list, followed by a plain section.</summary>
    public static WorkflowReport GridThenSection()
    {
        var report = new WorkflowReport { Title = "T" };
        var grid = new ReportSection
        {
            Title = "News & Market Data",
            Layout = new GridLayoutOutput
            {
                Mode = GridLayoutOutput.GRID_MODE,
                Columns = 1,
                Elements = [new GridElementPosition { BlockOrder = 0, Row = 1, Column = 1, ColumnSpan = 1, RowSpan = 1 }],
            },
        };
        report.Add(grid);
        grid.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Order = 0, Subtitle = "News & Issues", Content = "- item one\n- " + GRID_LAST_ITEM });
        var next = new ReportSection { Title = SECTION_AFTER_GRID };
        report.Add(next);
        next.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Order = 0, Content = "Governance body text." });
        return report;
    }
}
