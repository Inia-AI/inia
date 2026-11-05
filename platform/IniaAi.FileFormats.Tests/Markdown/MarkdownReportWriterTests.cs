using IniaAi.Common.Constants;
using IniaAi.Common.Content;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.FileFormats.Markdown;
using IniaAi.FileFormats.Word;

namespace IniaAi.FileFormats.Tests.Markdown;

public class MarkdownReportWriterTests
{
    private const string SECTION_TITLE = "Summary";

    [Fact]
    public void Write_ReportHeader_WritesTitleCounterpartyAndAnalyst()
    {
        var root = new DocumentElement { Title = "Credit Review" };
        root.Metadata[DocumentMetadataKeys.COUNTERPARTY] = "Acme Holdings";
        root.Metadata[DocumentMetadataKeys.ANALYST_NAME] = "Jane Analyst";

        string markdown = MarkdownReportWriter.Write(root);

        Assert.Equal($"# Credit Review\n\n**{CounterpartyTerms.SINGULAR}:** Acme Holdings  \n**Analyst:** Jane Analyst\n", markdown);
    }

    [Fact]
    public async Task WriteAsync_WithAFooterNotice_EndsWithTheNoticeBelowARule()
    {
        var root = new DocumentElement { Title = "Credit Review" };

        BinaryContent file = await new MarkdownReportWriter().WriteAsync(root, new ReportFileOptions(FooterNotice: "Check the *figures* before you rely on them."));

        Assert.Equal("# Credit Review\n\n---\n\nCheck the \\*figures\\* before you rely on them.\n", file.BinaryData.ToString());
    }

    [Fact]
    public async Task WriteAsync_WithoutAFooterNotice_WritesWhatWriteWrites()
    {
        var root = new DocumentElement { Title = "Credit Review" };

        BinaryContent file = await new MarkdownReportWriter().WriteAsync(root, new ReportFileOptions());

        Assert.Equal("# Credit Review\n", file.BinaryData.ToString());
        Assert.Equal(MarkdownReportWriter.Write(root), file.BinaryData.ToString());
    }

    [Fact]
    public void Write_MarkdownContent_KeepsHeadingsListsEmphasisLinksAndQuotes()
    {
        const string source = """
            # Executive Summary

            **Acme Industries Ltd.** is a *mid-cap* manufacturer. See [methodology](https://example.com/rating-methodology).

            ## Key Highlights

            - Revenue grew **18%**
            - EBITDA margin `22.4%`
              - up from 19.1%
            - ~~declining~~ *volatile* cash conversion

            1. Stable demand
            2. Diversified base

            > **Analyst note:** cyclical sector.

            ---

            - [x] Legal due diligence
            - [ ] Insurance certificates
            """;

        string markdown = WriteParagraph(MarkdownRenderer.ToHtmlForWordExport(source));

        Assert.Equal(
            """
            ## Summary

            #### Executive Summary

            **Acme Industries Ltd.** is a *mid-cap* manufacturer. See [methodology](https://example.com/rating-methodology).

            ##### Key Highlights

            - Revenue grew **18%**
            - EBITDA margin `22.4%`
              - up from 19.1%
            - ~~declining~~ *volatile* cash conversion

            1. Stable demand
            2. Diversified base

            > **Analyst note:** cyclical sector.

            ---

            - [x] Legal due diligence
            - [ ] Insurance certificates

            """,
            markdown);
    }

    [Fact]
    public void Write_HtmlTable_KeepsEveryCellValueAndEscapesPipes()
    {
        const string html = """
            <table style="border-collapse:collapse"><thead><tr><th>Item</th><th>FY24</th></tr></thead>
            <tbody><tr><td colspan="2">CASH FLOWS</td></tr>
            <tr><td>Revenue | net</td><td><strong>312</strong></td></tr>
            <tr><td>Margin<br>after tax</td><td>&nbsp;</td></tr></tbody></table>
            """;

        string markdown = WriteParagraph(html);

        Assert.Equal(
            """
            ## Summary

            | Item | FY24 |
            | --- | --- |
            | CASH FLOWS |  |
            | Revenue \| net | **312** |
            | Margin<br>after tax |  |

            """,
            markdown);
    }

    [Fact]
    public void Write_TableCellThatStartsLikeABlock_StaysUnescaped()
    {
        const string html = "<table><tr><th>Item</th><th>FY24</th></tr><tr><td>- </td><td>1. Revenue</td></tr></table>";

        string markdown = WriteParagraph(html);

        Assert.Equal("## Summary\n\n| Item | FY24 |\n| --- | --- |\n| - | 1. Revenue |\n", markdown);
    }

    [Fact]
    public void Write_PipeTableFromMarkdown_BecomesAMarkdownTable()
    {
        const string source = """
            | Metric | FY2024 | FY2025 |
            |--------|-------:|-------:|
            | Revenue ($M) | 264 | 312 |
            | EBITDA ($M) | 52.1 | 69.9 |
            """;

        string markdown = WriteParagraph(MarkdownRenderer.ToHtmlForWordExport(source));

        Assert.Equal(
            """
            ## Summary

            | Metric | FY2024 | FY2025 |
            | --- | --- | --- |
            | Revenue ($M) | 264 | 312 |
            | EBITDA ($M) | 52.1 | 69.9 |

            """,
            markdown);
    }

    [Fact]
    public void Write_TextWithMarkdownCharacters_EscapesThem()
    {
        const string html = "<p>5 * 3 = 15, snake_case, _x_, [y], &lt;tag&gt;, ~45%, a\\b, `x`</p>";

        string markdown = WriteParagraph(html);

        Assert.Equal("## Summary\n\n5 \\* 3 = 15, snake_case, \\_x\\_, \\[y\\], \\<tag>, \\~45%, a\\\\b, \\`x\\`\n", markdown);
    }

    [Fact]
    public void Write_LineThatWouldStartABlock_EscapesItsMarker()
    {
        const string html = "<p>- not a list<br />1. not numbered<br /># not a heading<br />&gt; not a quote</p>";

        string markdown = WriteParagraph(html);

        Assert.Equal("## Summary\n\n\\- not a list  \n1\\. not numbered  \n\\# not a heading  \n\\> not a quote\n", markdown);
    }

    [Fact]
    public void Write_Pictures_LeavesNoPictureInTheFile()
    {
        const string source = """
            Before

            <img src="data:image/png;base64,iVBORw0KGgo=" alt="Embedded chart">

            Logo: ![Acme logo](https://example.com/logo.png)

            After
            """;

        string markdown = WriteParagraph(MarkdownRenderer.ToHtmlForWordExport(source));

        Assert.Equal("## Summary\n\nBefore\n\nLogo: Acme logo\n\nAfter\n", markdown);
    }

    [Fact]
    public void Write_Chart_BecomesACaptionAndADataTable()
    {
        var chart = new DocumentElement { Type = DocumentElementType.Chart, Content = "Chart: Liquidity" };
        chart.OutputData.Add(new ChartOutput
        {
            Name = "Liquidity",
            ChartData =
            [
                new ChartDataPoint { X = "2023", Y = 1.6, Series = "Current Ratio" },
                new ChartDataPoint { X = "2024", Y = 1.8, Series = "Current Ratio" },
                new ChartDataPoint { X = "2023", Y = 1, Series = "Quick Ratio" },
            ],
        });

        string markdown = MarkdownReportWriter.Write(Report(Section(chart)));

        Assert.Equal(
            """
            ## Summary

            Chart: Liquidity

            | Category | Current Ratio | Quick Ratio |
            | --- | --- | --- |
            | 2023 | 1.6 | 1 |
            | 2024 | 1.8 |  |

            """,
            markdown);
    }

    [Fact]
    public void Write_ChartWithoutData_SaysSo()
    {
        var chart = new DocumentElement { Type = DocumentElementType.Chart };
        chart.OutputData.Add(new ChartOutput { Name = "Empty" });

        string markdown = MarkdownReportWriter.Write(Report(Section(chart)));

        Assert.Equal("## Summary\n\nChart: Empty\n\nNo data available\n", markdown);
    }

    [Fact]
    public void Write_SubtitleDividerAndTableParagraphs_KeepTheirOrder()
    {
        var subtitled = new DocumentElement
        {
            Type = DocumentElementType.Paragraph,
            Title = "Block title the Word export leaves out",
            Subtitle = "Peer comparison",
            Content = "<p>Peers trade at 6x.</p>",
            Order = 0,
        };
        var divider = new DocumentElement { Type = DocumentElementType.Divider, Order = 1 };
        var table = new DocumentElement
        {
            Type = DocumentElementType.Table,
            Content = "<table><tr><th>Peer</th><th>Multiple</th></tr><tr><td>Beta</td><td>6.1x</td></tr></table>",
            Order = 2,
        };

        string markdown = MarkdownReportWriter.Write(Report(Section(subtitled, divider, table)));

        Assert.Equal(
            """
            ## Summary

            ### Peer comparison

            Peers trade at 6x.

            ---

            | Peer | Multiple |
            | --- | --- |
            | Beta | 6.1x |

            """,
            markdown);
    }

    [Fact]
    public void Write_GridSection_ReadsRowByRowThenTheUnplacedBlocks()
    {
        DocumentElement section = Section(
            Paragraph("<p>Block zero</p>", 0),
            Paragraph("<p>Block one</p>", 1),
            Paragraph("<p>Block two</p>", 2),
            Paragraph("<p>Block three</p>", 3));
        section.OutputData.Add(new GridLayoutOutput
        {
            Mode = "grid",
            Columns = 2,
            Elements =
            [
                new GridElementPosition { BlockOrder = 2, Row = 1, Column = 1 },
                new GridElementPosition { BlockOrder = 0, Row = 1, Column = 2 },
                new GridElementPosition { BlockOrder = 1, Row = 2, Column = 1 },
            ],
        });

        string markdown = MarkdownReportWriter.Write(Report(section));

        Assert.Equal("## Summary\n\nBlock two\n\nBlock zero\n\nBlock one\n\nBlock three\n", markdown);
    }

    [Fact]
    public void Write_UnclosedAndStrayTags_KeepEveryWord()
    {
        const string html = "<p>First <strong>bold<p>Second</em> paragraph<ul><li>one<li>two</ul></div>";

        string markdown = WriteParagraph(html);

        Assert.Equal("## Summary\n\nFirst **bold**\n\nSecond paragraph\n\n- one\n- two\n", markdown);
    }

    [Fact]
    public void Write_UnknownTagInsideASentence_StaysInTheSentence()
    {
        string markdown = WriteParagraph("<p>The <rating>BBB</rating> rating holds.</p>");

        Assert.Equal("## Summary\n\nThe BBB rating holds.\n", markdown);
    }

    [Fact]
    public void Write_InlineSvg_LeavesNoPictureText()
    {
        string markdown = WriteParagraph("<p>Before</p><svg viewBox=\"0 0 10 10\"><title>Revenue chart</title><text x=\"1\">42</text></svg><p>After</p>");

        Assert.Equal("## Summary\n\nBefore\n\nAfter\n", markdown);
    }

    [Fact]
    public void Write_BlockInAnUnknownTag_KeepsItsTextAsHtmlWouldShowIt()
    {
        const string source = """
            <json>
            {
              "currency": "EUR"
            }
            </json>
            """;

        string markdown = WriteParagraph(MarkdownRenderer.ToHtmlForWordExport(source));

        Assert.Equal("## Summary\n\n{ \"currency\": \"EUR\" }\n", markdown);
    }

    [Fact]
    public void Write_TitleTag_KeepsItsText()
    {
        string markdown = WriteParagraph("<p>Before</p><title>Quarterly view</title>");

        Assert.Equal("## Summary\n\nBefore\n\nQuarterly view\n", markdown);
    }

    [Fact]
    public void Write_TableInsideAListItem_StaysInItsItem()
    {
        const string html = "<ul><li>Peers<table><tr><th>Name</th><th>Multiple</th></tr><tr><td>Beta</td><td>6.1x</td></tr></table></li><li>Next item</li></ul>";

        string markdown = WriteParagraph(html);

        Assert.Equal(
            """
            ## Summary

            - Peers

              | Name | Multiple |
              | --- | --- |
              | Beta | 6.1x |
            - Next item

            """,
            markdown);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,hello")]
    public void Write_UnsafeLink_KeepsOnlyItsText(string href)
    {
        string markdown = WriteParagraph($"<p><a href=\"{href}\">Click here</a></p>");

        Assert.Equal("## Summary\n\nClick here\n", markdown);
    }

    [Fact]
    public void Write_HttpMailtoAndRelativeLinks_StayLinks()
    {
        const string html = "<p><a href=\"https://example.com/a\">Site</a> <a href=\"mailto:credit@example.com\">Mail</a> <a href=\"/reports/42\">Section</a></p>";

        string markdown = WriteParagraph(html);

        Assert.Equal("## Summary\n\n[Site](https://example.com/a) [Mail](mailto:credit@example.com) [Section](/reports/42)\n", markdown);
    }

    [Theory]
    [InlineData("span")]
    [InlineData("b")]
    public void Write_FiveThousandNestedTags_KeepsTheTextWithoutOverflowingTheStack(string tag)
    {
        string html = $"<p>{string.Concat(Enumerable.Repeat($"<{tag}>", 5000))}deep</p>";

        string markdown = WriteParagraph(html);

        Assert.Contains("deep", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_AdjacentBoldRuns_BecomeOneRun()
    {
        string markdown = WriteParagraph("<p><strong>Revenue:</strong><strong>12</strong> million</p>");

        Assert.Equal("## Summary\n\n**Revenue:12** million\n", markdown);
    }

    [Fact]
    public void Write_EmphasisThatMarkdownCannotOpenOrClose_StillRendersAsEmphasis()
    {
        string markdown = WriteParagraph("<p><strong>(a)</strong>b and c<em>(d)</em> and <del>[x]</del>y</p>");

        string html = MarkdownRenderer.ToHtml(markdown);
        Assert.Contains("<strong>(a)</strong>b", html, StringComparison.Ordinal);
        Assert.Contains("c<em>(d)</em>", html, StringComparison.Ordinal);
        Assert.Contains("<del>[x]</del>y", html, StringComparison.Ordinal);
        Assert.DoesNotContain("*", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Item #", "## Item \\#", "<h2>Item #</h2>")]
    [InlineData("#", "## \\#", "<h2>#</h2>")]
    public void Write_TitleEndingInHash_KeepsTheHash(string title, string heading, string rendered)
    {
        var section = new DocumentElement
        {
            Type = DocumentElementType.Section,
            Title = title,
            Children = [Paragraph("<p>Body</p>", 0)],
        };

        string markdown = MarkdownReportWriter.Write(Report(section));

        Assert.StartsWith($"{heading}\n", markdown, StringComparison.Ordinal);
        Assert.Contains(rendered, MarkdownRenderer.ToHtml(markdown), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_TableNestedInsideAListItem_GetsABlankLineBefore()
    {
        const string html = "<ul><li>Peers<div><table><tr><th>Name</th></tr><tr><td>Beta</td></tr></table></div></li></ul>";

        string markdown = WriteParagraph(html);

        Assert.Equal("## Summary\n\n- Peers\n\n  | Name |\n  | --- |\n  | Beta |\n", markdown);
    }

    [Fact]
    public void Write_PreformattedTextInATableCell_KeepsEachLineAsCode()
    {
        const string html = "<table><tr><th>Code</th></tr><tr><td><pre>a = 1\nb = 2</pre></td></tr></table>";

        string markdown = WriteParagraph(html);

        Assert.Equal("## Summary\n\n| Code |\n| --- |\n| `a = 1`<br>`b = 2` |\n", markdown);
    }

    private static string WriteParagraph(string html)
    {
        return MarkdownReportWriter.Write(Report(Section(Paragraph(html, 0))));
    }

    private static DocumentElement Report(DocumentElement section)
    {
        return new DocumentElement { Children = [section] };
    }

    private static DocumentElement Section(params DocumentElement[] children)
    {
        return new DocumentElement { Type = DocumentElementType.Section, Title = SECTION_TITLE, Children = [.. children] };
    }

    private static DocumentElement Paragraph(string html, int order)
    {
        return new DocumentElement { Type = DocumentElementType.Paragraph, Content = html, Order = order };
    }
}
