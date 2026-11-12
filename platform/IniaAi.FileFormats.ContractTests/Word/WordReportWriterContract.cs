using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using IniaAi.Common.Content;
using IniaAi.Common.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats.ContractTests.Reports;
using IniaAi.FileFormats.Markdown;
using IniaAi.FileFormats.Word;
using IniaAi.Testing.Ooxml;

namespace IniaAi.FileFormats.ContractTests.Word;

/// <summary>
/// What a run's Word report holds and how it looks, whatever library writes it: the report export relies on these
/// from every <see cref="IReportFileWriter"/> of the Word format, on top of what every writer owes. Each case writes a
/// report through <see cref="WorkflowDocumentAdapter"/> with the options the export passes, and reads the .docx as Word
/// shows it.
/// </summary>
public abstract partial class WordReportWriterContract : ReportFileWriterContract
{
    private const double BODY_FONT_SIZE = 11;
    private const double TITLE_FONT_SIZE = 14;
    private const string SUBTITLE_BLUE = "365F91";
    private const string BODY_FONT = "Calibri";
    private const string JUSTIFIED = "both";
    private const string HEADING_STYLE = "heading";
    private const int MARKER_AT_MARGIN_TWIPS = 80;
    private const int FULL_WIDTH_TWIPS = 8_000;
    private const int LETTER_LONG_EDGE_TWIPS = 15_840;
    private const int LETTER_SHORT_EDGE_TWIPS = 12_240;
    private const int PAPER_TOLERANCE_TWIPS = 30;
    private const string LANDSCAPE = "Landscape";
    private const string LETTER = "Letter";
    private const string NO_CHART = "This report has no chart.";
    private const string FOOTER_NOTICE = "Check important figures against the source documents.";
    private const string PAGE_PREFIX = "Page ";
    private const string PAGE_FIELD = "PAGE";
    private const string PAGE_COUNT_FIELD = "NUMPAGES";
    private const string ONE_PIXEL_PNG = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    private const string MINIMAL = "minimal";
    private const string HTML_TABLES = "html tables";
    private const string MARKDOWN_BASICS = "markdown basics";
    private const string RICH_MARKDOWN = "rich markdown";
    private const string INDUSTRY = "industry";
    private const string NEWS_GRID = "news grid with a chart";
    private const string STYLING = "styling";
    private const string CREDIT_REVIEW = "credit review";

    private static readonly Dictionary<string, Func<WorkflowReport>> _reports = new()
    {
        [MINIMAL] = SmallReportFixtures.Minimal,
        [HTML_TABLES] = SmallReportFixtures.HtmlTables,
        [MARKDOWN_BASICS] = SmallReportFixtures.MarkdownBasics,
        [RICH_MARKDOWN] = RichMarkdownReportFixture.Build,
        [INDUSTRY] = IndustryReportFixture.Build,
        [NEWS_GRID] = NewsMarketDataFixture.Build,
        [STYLING] = StylingReportFixture.Build,
        [CREDIT_REVIEW] = CreditReviewReportFixture.Build,
    };

    // Several cases read the same report, and a write takes up to a second, so each writer writes each named report once.
    private static readonly ConcurrentDictionary<(Type Contract, string Report), Lazy<Task<byte[]>>> _written = new();

    public static TheoryData<string> ReportNames => [.. _reports.Keys];

    [Theory]
    [MemberData(nameof(ReportNames))]
    public async Task WriteAsync_WritesAWordDocument_ThatTheSchemaAccepts(string reportName)
    {
        byte[] file = await WrittenAsync(reportName);

        Assert.Equal(OoxmlPackage.DOCUMENT_TYPE, OoxmlPackage.ContentType(file, OoxmlPackage.DOCUMENT_PART));
        using var stream = new MemoryStream(file, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, isEditable: false);
        Assert.Empty(new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document).Select(error => $"{error.Path?.XPath}: {error.Description}"));
    }

    [Fact]
    public async Task WriteAsync_CarriesTheSectionsAndValuesOfTheMarkdownExport_InTheSameOrder()
    {
        string[] inOrder = ["Executive Summary", "Net leverage is", "Financials", "Revenue", "264", "312", "EBITDA", "52.1", "69.9"];
        BinaryContent markdownFile = await new MarkdownReportWriter().WriteAsync(WorkflowDocumentAdapter.ToDocumentElement(CreditReviewReportFixture.Build()), new ReportFileOptions());

        string markdown = Encoding.UTF8.GetString(markdownFile.BinaryData.ToArray());
        string word = (await ReadAsync(CREDIT_REVIEW)).Text;

        AssertAppearInOrder(markdown, inOrder);
        AssertAppearInOrder(word, inOrder);
        Assert.DoesNotContain(CreditReviewReportFixture.EXCLUDED_SECTION_TITLE, word, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_WithAFooterNotice_PutsItInTheFooterOfEveryPage_AbovePageXOfY()
    {
        byte[] file = await WriteBytesAsync(Writer, CreditReviewReportFixture.Build(), new ReportFileOptions(FooterNotice: FOOTER_NOTICE));

        IReadOnlyList<XDocument> footers = PageFooters(file);
        Assert.NotEmpty(footers);
        Assert.All(footers, footer =>
        {
            IReadOnlyList<string> lines = OoxmlPackage.FooterLines(footer);
            Assert.Equal(FOOTER_NOTICE, lines[0]);
            Assert.StartsWith(PAGE_PREFIX, lines[^1], StringComparison.Ordinal);
            Assert.Equal([PAGE_FIELD, PAGE_COUNT_FIELD], OoxmlPackage.FieldCodes(footer));
        });
        Assert.DoesNotContain(FOOTER_NOTICE, new WordFile(file).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithoutAFooterNotice_HasOnlyPageXOfYInTheFooter()
    {
        byte[] file = await WrittenAsync(CREDIT_REVIEW);

        IReadOnlyList<XDocument> footers = PageFooters(file);
        Assert.NotEmpty(footers);
        Assert.All(footers, footer =>
        {
            string line = Assert.Single(OoxmlPackage.FooterLines(footer));
            Assert.StartsWith(PAGE_PREFIX, line, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task WriteAsync_ShowsTheAnalystFromTheReportsMetadata()
    {
        WordFile file = await ReadAsync(CREDIT_REVIEW);

        Assert.Contains(CreditReviewReportFixture.ANALYST, file.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_LaysThePagesOutAsTheReportLayoutAsks()
    {
        WordFile file = await WriteAsync(CreditReviewReportFixture.Build(), new ReportFileOptions(ReportLayout: new ReportLayout { Orientation = LANDSCAPE, PageSize = LETTER }));

        Assert.NotEmpty(file.PageSizes);
        Assert.All(file.PageSizes, pageSize =>
        {
            Assert.InRange(OoxmlPackage.Twips(pageSize, "w")!.Value, LETTER_LONG_EDGE_TWIPS - PAPER_TOLERANCE_TWIPS, LETTER_LONG_EDGE_TWIPS + PAPER_TOLERANCE_TWIPS);
            Assert.InRange(OoxmlPackage.Twips(pageSize, "h")!.Value, LETTER_SHORT_EDGE_TWIPS - PAPER_TOLERANCE_TWIPS, LETTER_SHORT_EDGE_TWIPS + PAPER_TOLERANCE_TWIPS);
        });
    }

    [Fact]
    public async Task WriteAsync_PutsInTheChartPictureThatTheChartRendererDraws()
    {
        WordFile withoutChart = await WriteAsync(SmallReportFixtures.OneParagraph(NO_CHART));
        WordFile file = await WriteAsync(NewsMarketDataFixture.ChartOnly(), new ReportFileOptions(new ChromeThatDraws()));

        Assert.Equal(withoutChart.Drawings + 1, file.Drawings);
    }

    [Fact]
    public async Task WriteAsync_PutsInATableOfTheChartsData_WhenTheChartRendererFails()
    {
        WordFile withoutChart = await WriteAsync(SmallReportFixtures.OneParagraph(NO_CHART));
        WordFile file = await WriteAsync(NewsMarketDataFixture.ChartOnly(), new ReportFileOptions(new ChromeThatCannotStart()));

        Assert.Equal(withoutChart.Drawings, file.Drawings);
        Assert.Equal(withoutChart.Tables + 1, file.Tables);
    }

    [Fact]
    public async Task WriteAsync_PutsInATableOfTheChartsData_WhenTheBuildHasNoChartRenderer()
    {
        WordFile withoutChart = await WriteAsync(SmallReportFixtures.OneParagraph(NO_CHART));
        WordFile file = await WriteAsync(NewsMarketDataFixture.ChartOnly());

        Assert.Equal(withoutChart.Drawings, file.Drawings);
        Assert.Equal(withoutChart.Tables + 1, file.Tables);
    }

    [Fact]
    public async Task WriteAsync_TurnsUnsafeAndRelativeLinksIntoPlainText_AndKeepsTheOthers()
    {
        WordFile file = await WriteAsync(CreditReviewReportFixture.WithLinks());

        Assert.Contains("Run the script, open the page or see", file.Text, StringComparison.Ordinal);
        Assert.Contains("and the summary.", file.Text, StringComparison.Ordinal);
        Assert.Equal(["https://example.com/method", "mailto:desk@example.com"], file.HyperlinkAddresses);
    }

    [Fact]
    public async Task WriteAsync_TurnsMarkdownLinksIntoHyperlinks()
    {
        WordFile file = await ReadAsync(RICH_MARKDOWN);

        Assert.Contains("https://example.com/rating-methodology", file.HyperlinkAddresses);
        Assert.Contains("mailto:credit@example.com", file.HyperlinkAddresses);
    }

    [Fact]
    public async Task WriteAsync_GivesMarkdownHeadingsAWordHeadingStyle_LargerThanTheBody()
    {
        WordFile file = await ReadAsync(RICH_MARKDOWN);

        List<XElement> headings = [.. file.Paragraphs.Where(paragraph => file.StyleName(paragraph)?.Contains(HEADING_STYLE, StringComparison.OrdinalIgnoreCase) == true)];
        Assert.NotEmpty(headings);
        Assert.Contains(headings, heading => file.ShowsPhrase(heading, WordFile.TextOf(heading).Trim(), run => file.FontSize(run) > BODY_FONT_SIZE));
    }

    [Fact]
    public async Task WriteAsync_KeepsInlineBoldAndItalic()
    {
        WordFile file = await ReadAsync(RICH_MARKDOWN);

        Assert.True(file.Shows("year-over-year", file.IsBold));
        Assert.True(file.Shows("revolving facility", file.IsItalic));
    }

    [Theory]
    [InlineData(HTML_TABLES, "Metric")]
    [InlineData(MARKDOWN_BASICS, "Col A")]
    public async Task WriteAsync_ShowsATablesHeaderCells_InBold(string reportName, string headerCell)
    {
        WordFile file = await ReadAsync(reportName);

        Assert.True(file.Shows(headerCell, file.IsBold));
    }

    [Fact]
    public async Task WriteAsync_ShowsStruckThroughMarkdown_AsStruckThroughText_NotAsATrackedDeletion()
    {
        WordFile file = await ReadAsync(RICH_MARKDOWN);

        Assert.True(file.Shows("declining", file.IsStruckThrough));
    }

    [Fact]
    public async Task WriteAsync_KeepsABoldLeadIn()
    {
        WordFile file = await ReadAsync(INDUSTRY);

        Assert.True(file.Shows("Escalation index mismatch", file.IsBold));
    }

    [Theory]
    [InlineData(RICH_MARKDOWN, 3)]
    [InlineData(INDUSTRY, 5)]
    public async Task WriteAsync_TurnsOrderedListsIntoNumberedListItems(string reportName, int numberedItems)
    {
        WordFile file = await ReadAsync(reportName);

        Assert.True(file.Paragraphs.Count(file.IsNumberedListItem) >= numberedItems, $"Fewer than {numberedItems} numbered list items.");
    }

    [Fact]
    public async Task WriteAsync_LeavesNoListNumberAsText_InAListSplitByOtherParagraphs()
    {
        WordFile file = await ReadAsync(INDUSTRY);

        Assert.DoesNotContain(file.Paragraphs, paragraph => !file.IsListItem(paragraph) && OrdinalText().IsMatch(WordFile.TextOf(paragraph).Trim()));
    }

    [Fact]
    public async Task WriteAsync_GivesEachPictureItsOwnId_WhenATableOfTheContentHoldsOne()
    {
        string picture = $"<img src=\"data:image/png;base64,{ONE_PIXEL_PNG}\">";
        WorkflowReport report = SmallReportFixtures.OneParagraph($"<table border=\"1\"><tr><td>{picture}</td></tr></table><p>{picture}</p>");

        byte[] file = await WriteBytesAsync(Writer, report, new ReportFileOptions());

        XNamespace wordprocessingDrawing = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        List<string?> ids = [.. OoxmlPackage.Part(file, OoxmlPackage.DOCUMENT_PART).Descendants(wordprocessingDrawing + "docPr").Select(properties => (string?)properties.Attribute("id"))];
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task WriteAsync_InAGrid_KeepsABlockPlacedPastTheLastColumn()
    {
        WordFile file = await WriteAsync(SmallReportFixtures.GridWithABlockPastItsLastColumn());

        Assert.Contains(SmallReportFixtures.GRID_FIRST_COLUMN_TEXT, file.Text, StringComparison.Ordinal);
        Assert.Contains(SmallReportFixtures.GRID_PAST_LAST_COLUMN_TEXT, file.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_InAGrid_GivesAFullWidthBlockTheWholePage()
    {
        WordFile file = await ReadAsync(NEWS_GRID);

        AssertFullWidth(file.ParagraphsWith("Consent Order").First());
    }

    [Fact]
    public async Task WriteAsync_InAGrid_GivesTheWholePageToABlockWhoseChartTheReportLeftOut()
    {
        WorkflowReport report = NewsMarketDataFixture.Build();
        ReportSection section = Assert.Single(report.Sections);
        _ = section.Paragraphs.Remove(section.Paragraphs.Single(paragraph => paragraph.Kind is ReportParagraphKind.DynamicChart));

        WordFile file = await WriteAsync(report);

        AssertFullWidth(file.ParagraphsWith("Harrowgate Aerostructures plc").First(paragraph => !WordFile.TextOf(paragraph).Contains("Consent Order", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WriteAsync_InAGrid_FloatsTheChartPicture_SoTheTextBesideItTakesTheFullWidth()
    {
        WordFile file = await WriteAsync(NewsMarketDataFixture.Build(), new ReportFileOptions(new ChromeThatDraws()));

        Assert.True(file.HasFloatingShape, "The chart sits in a line of text instead of floating.");
        AssertFullWidth(file.ParagraphsWith("Harrowgate Aerostructures plc").First(paragraph => !WordFile.TextOf(paragraph).Contains("Consent Order", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WriteAsync_ShowsSectionTitlesInBoldCapitals_At14Points()
    {
        WordFile file = await ReadAsync(STYLING);

        Assert.True(file.Shows("Financial Evaluation", run => file.IsAllCaps(run) && file.IsBold(run) && file.FontSize(run) == TITLE_FONT_SIZE));
    }

    [Theory]
    [InlineData(STYLING, "Profitability")]
    [InlineData(NEWS_GRID, "News & Issues")]
    public async Task WriteAsync_ShowsSubtitlesInBoldBlue_At14Points(string reportName, string subtitle)
    {
        WordFile file = await ReadAsync(reportName);

        Assert.True(file.Shows(subtitle, run => file.IsBold(run) && file.Colour(run) == SUBTITLE_BLUE && file.FontSize(run) == TITLE_FONT_SIZE));
    }

    [Fact]
    public async Task WriteAsync_DrawsAHorizontalRuleBeforeEachSectionTitle_AndNoneBeforeItsFirstSubtitle()
    {
        WordFile file = await ReadAsync(STYLING);

        int title = file.IndexOfParagraphWith("Counterparty Overview");
        int subtitle = file.IndexOfParagraphWith("Strengths & Weaknesses");
        Assert.Equal(StylingReportFixture.Sections.Length, file.HorizontalRules);
        Assert.True(title >= 0 && subtitle > title);
        Assert.Contains(file.Paragraphs.Take(title), file.HasHorizontalRule);
        Assert.DoesNotContain(Between(file, title, subtitle), file.HasHorizontalRule);
    }

    [Fact]
    public async Task WriteAsync_JustifiesBodyTextAndListItems_ButNotTitles()
    {
        WordFile file = await ReadAsync(STYLING);

        Assert.Equal(JUSTIFIED, file.Alignment(file.ParagraphsWith("was founded on").First()));
        Assert.NotEqual(JUSTIFIED, file.Alignment(file.ParagraphsWith("Financial Evaluation").First()));
        List<XElement> listItems = [.. file.Paragraphs.Where(file.IsListItem)];
        Assert.NotEmpty(listItems);
        Assert.All(listItems, item => Assert.Equal(JUSTIFIED, file.Alignment(item)));
    }

    [Fact]
    public async Task WriteAsync_SetsListNumbersInTheBodyFont_AndTopLevelMarkersAtTheMargin()
    {
        WordFile file = await ReadAsync(STYLING);

        XElement numbered = file.Paragraphs.First(file.IsNumberedListItem);
        Assert.Equal((BODY_FONT, BODY_FONT_SIZE), file.MarkerFont(numbered));
        List<XElement> topLevel = [.. file.Paragraphs.Where(paragraph => file.IsListItem(paragraph) && file.ListLevelNumber(paragraph) == 0)];
        Assert.NotEmpty(topLevel);
        Assert.All(topLevel, item => Assert.True(file.MarkerPosition(item) <= MARKER_AT_MARGIN_TWIPS, $"A top-level list marker sits {file.MarkerPosition(item)} twips in from the margin."));
    }

    [Fact]
    public async Task WriteAsync_TrimsWhiteSpaceAroundTitlesSubtitlesAndText()
    {
        WordFile file = await WriteAsync(SmallReportFixtures.Padded());

        List<string> texts = [.. file.Paragraphs.Select(WordFile.TextOf)];
        Assert.Contains(SmallReportFixtures.PADDED_SECTION, texts);
        Assert.Contains(SmallReportFixtures.PADDED_SUBTITLE, texts);
        Assert.Contains(SmallReportFixtures.PADDED_BODY, texts);
    }

    [Fact]
    public async Task WriteAsync_MakesALineThatEndsInABreak_IntoNoExtraBlankParagraph()
    {
        WordFile without = await WriteAsync(SmallReportFixtures.OneParagraph("**Heading**\nBody line that wraps here."));
        WordFile with = await WriteAsync(SmallReportFixtures.OneParagraph("**Heading**\nBody line that wraps here.\n"));

        Assert.Equal(without.Paragraphs.Count(without.IsBlankBodyParagraph), with.Paragraphs.Count(with.IsBlankBodyParagraph));
    }

    [Fact]
    public async Task WriteAsync_PutsNoBlankParagraphBetweenAGridAndTheNextSection()
    {
        WordFile file = await WriteAsync(SmallReportFixtures.GridThenSection());

        int lastGridItem = file.LastIndexOfParagraphWith(SmallReportFixtures.GRID_LAST_ITEM);
        int nextTitle = file.IndexOfParagraphWith(SmallReportFixtures.SECTION_AFTER_GRID);
        Assert.True(lastGridItem >= 0 && nextTitle > lastGridItem);
        Assert.DoesNotContain(Between(file, lastGridItem, nextTitle), file.IsBlankBodyParagraph);
    }

    [Fact]
    public async Task WriteAsync_StartsANewParagraphAtEachLineBreakOfTheBody()
    {
        WordFile file = await ReadAsync(STYLING);

        Assert.Contains(file.Paragraphs, paragraph => WordFile.TextOf(paragraph).Trim() == "Rating Rationale:");
        Assert.DoesNotContain(file.Paragraphs, paragraph => !WordFile.IsInTable(paragraph) && WordFile.HasLineBreak(paragraph));
    }

    private static IEnumerable<XElement> Between(WordFile file, int first, int last)
    {
        return file.Paragraphs.Skip(first + 1).Take(last - first - 1);
    }

    private static void AssertFullWidth(XElement paragraph)
    {
        int? width = WordFile.CellWidth(paragraph);
        Assert.True(width is null or >= FULL_WIDTH_TWIPS, $"The block sits in a {width}-twip column instead of spanning the page.");
    }

    private static void AssertAppearInOrder(string text, string[] fragments)
    {
        int position = 0;
        foreach (string fragment in fragments)
        {
            int found = text.IndexOf(fragment, position, StringComparison.OrdinalIgnoreCase);
            Assert.True(found >= 0, $"'{fragment}' does not follow position {position}.");
            position = found + fragment.Length;
        }
    }

    private static async Task<byte[]> WriteBytesAsync(IReportFileWriter writer, WorkflowReport report, ReportFileOptions options)
    {
        BinaryContent file = await writer.WriteAsync(WorkflowDocumentAdapter.ToDocumentElement(report), options);
        return file.BinaryData.ToArray();
    }

    private static IReadOnlyList<XDocument> PageFooters(byte[] file)
    {
        return [.. OoxmlPackage.Sections(file).Select(section => OoxmlPackage.Footer(file, section)).OfType<XDocument>()];
    }

    private async Task<WordFile> ReadAsync(string reportName)
    {
        return new WordFile(await WrittenAsync(reportName));
    }

    private Task<byte[]> WrittenAsync(string reportName)
    {
        IReportFileWriter writer = Writer;
        return _written.GetOrAdd((GetType(), reportName), _ => new Lazy<Task<byte[]>>(() => WriteBytesAsync(writer, _reports[reportName](), new ReportFileOptions()))).Value;
    }

    [Fact]
    public async Task WriteAsync_FetchesNothingThatTheReportContentNames()
    {
        using var probe = new LoopbackProbe();
        string content = $"{RemoteContent.Head(probe.Address)}<p>Figures</p>{RemoteContent.Body(probe.Address)}\n\n![Logo]({probe.Address}logo.png)";

        _ = await WriteAsync(SmallReportFixtures.OneParagraph(content)).WaitAsync(EntityPackages.ReadTimeLimit, TestContext.Current.CancellationToken);

        Assert.True(probe.Connections == 0, $"Fetched: {string.Join("; ", probe.Requests)}");
    }

    private async Task<WordFile> WriteAsync(WorkflowReport report, ReportFileOptions? options = null)
    {
        return new WordFile(await WriteBytesAsync(Writer, report, options ?? new ReportFileOptions()));
    }

    [GeneratedRegex(@"^\d+\.\s")]
    private static partial Regex OrdinalText();

    /// <summary>Chrome, which draws each chart picture for the export, is an outside process; this one draws a fixed picture.</summary>
    private sealed class ChromeThatDraws : IChartImageRenderService
    {
        public Task<byte[]> RenderChartToImageAsync(object chartConfig, int width, int height, string? reportContext = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Convert.FromBase64String(ONE_PIXEL_PNG));
        }
    }

    /// <summary>Chrome, which draws each chart picture for the export, is an outside process; here it fails to start.</summary>
    private sealed class ChromeThatCannotStart : IChartImageRenderService
    {
        public Task<byte[]> RenderChartToImageAsync(object chartConfig, int width, int height, string? reportContext = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Chrome did not start.");
        }
    }
}
