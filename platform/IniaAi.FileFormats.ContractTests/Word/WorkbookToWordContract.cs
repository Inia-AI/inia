using System.Xml.Linq;
using IniaAi.FileFormats.Word;
using IniaAi.Testing.Ooxml;
using static IniaAi.FileFormats.ContractTests.TemplateFixtures;

namespace IniaAi.FileFormats.ContractTests.Word;

/// <summary>
/// What a workflow relies on from every <see cref="IWorkbookToWordConverter"/>. An implementation's test class
/// derives from this one and names the converter; the tests read only the produced file.
/// </summary>
public abstract class WorkbookToWordContract
{
    private const int LETTER_WIDTH_TWIPS = 12_240;
    private const int LETTER_HEIGHT_TWIPS = 15_840;
    private const int PAPER_TOLERANCE_TWIPS = 30;
    private const int ONE_INCH_TWIPS = 1_440;
    private const int INDENT_LEVEL_TWIPS = 288;
    private const string ROUND_BULLET = "\u2022";
    private const double PRINT_SCALE = 0.5;
    private const double PRINT_SCALE_TOLERANCE = 0.05;

    protected abstract IWorkbookToWordConverter Converter { get; }

    [Fact]
    public void ConvertToWord_GivesEachWordPageSheetASection_AndLeavesTheOtherSheetsOut()
    {
        byte[] document = Converter.ConvertToWord(CreditReviewTemplate);

        Assert.Equal(OoxmlPackage.DOCUMENT_TYPE, OoxmlPackage.ContentType(document, OoxmlPackage.DOCUMENT_PART));
        Assert.Equal(CREDIT_REVIEW_WORD_PAGES, OoxmlPackage.Part(document, OoxmlPackage.DOCUMENT_PART).Descendants(OoxmlPackage.WordprocessingMl + "sectPr").Count());
        string text = OoxmlPackage.AllText(document);
        Assert.Contains(CREDIT_REVIEW_COMPANY, text, StringComparison.Ordinal);
        Assert.DoesNotContain(CREDIT_REVIEW_LOGS_MARKER, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertToWord_WithoutWordPageSheets_ExportsTheFirstVisibleSheet_WithoutItsHiddenRowsAndColumns()
    {
        string text = OoxmlPackage.AllText(Converter.ConvertToWord(EditableWorkbook));

        Assert.Contains("Revenue", text, StringComparison.Ordinal);
        Assert.Contains("Large text", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden row text", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden column text", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Model text", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertToWord_ShowsTheValuesAsExcelShowsThem()
    {
        string text = OoxmlPackage.AllText(Converter.ConvertToWord(EditableWorkbook));

        Assert.Contains("25%", text, StringComparison.Ordinal);
        Assert.Contains("2025-09-30", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertToWord_TurnsMarkdownInACellIntoWordFormatting()
    {
        byte[] document = Converter.ConvertToWord(EditableWorkbook);

        List<XElement> paragraphs = [.. OoxmlPackage.WordParagraphs(document)];
        Assert.DoesNotContain(paragraphs, paragraph => OoxmlPackage.ParagraphText(paragraph).Contains("**Bold**", StringComparison.Ordinal));
        XElement point = paragraphs.Single(paragraph => OoxmlPackage.ParagraphText(paragraph) == "Bold point");
        var file = new WordFile(document);
        Assert.True(file.ShowsPhrase(point, "Bold", file.IsBold));
    }

    [Fact]
    public void ConvertToWord_WithAFooterNotice_PutsItAbovePageXOfY_OnEveryPage_AndWithoutOne_OnlyPageXOfY()
    {
        const string notice = "Check important figures against the source documents.";
        byte[] withNotice = Converter.ConvertToWord(EditableWorkbook, notice);
        byte[] without = Converter.ConvertToWord(EditableWorkbook);

        Assert.All(OoxmlPackage.Sections(withNotice), section =>
        {
            IReadOnlyList<string> lines = OoxmlPackage.FooterLines(OoxmlPackage.Footer(withNotice, section)!);
            Assert.Equal(notice, lines[0]);
            Assert.StartsWith("Page", lines[^1], StringComparison.Ordinal);
        });
        Assert.All(OoxmlPackage.Sections(without), section => Assert.StartsWith("Page", Assert.Single(OoxmlPackage.FooterLines(OoxmlPackage.Footer(without, section)!)), StringComparison.Ordinal));
        Assert.DoesNotContain(notice, OoxmlPackage.AllText(without), StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertToWord_GivesEveryPageLetterPaper_OneInchMargins_AndAPageXOfYFooter()
    {
        foreach (byte[] document in new[] { Converter.ConvertToWord(CreditReviewTemplate), Converter.ConvertToWord(EditableWorkbook) })
        {
            foreach (XElement section in OoxmlPackage.Sections(document))
            {
                XElement pageSize = section.Element(OoxmlPackage.WordprocessingMl + "pgSz")!;
                Assert.InRange(OoxmlPackage.Twips(pageSize, "w")!.Value, LETTER_WIDTH_TWIPS - PAPER_TOLERANCE_TWIPS, LETTER_WIDTH_TWIPS + PAPER_TOLERANCE_TWIPS);
                Assert.InRange(OoxmlPackage.Twips(pageSize, "h")!.Value, LETTER_HEIGHT_TWIPS - PAPER_TOLERANCE_TWIPS, LETTER_HEIGHT_TWIPS + PAPER_TOLERANCE_TWIPS);
                XElement margins = section.Element(OoxmlPackage.WordprocessingMl + "pgMar")!;
                Assert.All(["top", "bottom", "left", "right"], side => Assert.Equal(ONE_INCH_TWIPS, OoxmlPackage.Twips(margins, side)));
                XElement? titlePage = section.Element(OoxmlPackage.WordprocessingMl + "titlePg");
                Assert.True(
                    titlePage is null || (string?)titlePage.Attribute(OoxmlPackage.WordprocessingMl + "val") is "0" or "false" or "off",
                    "The first page of a section must show the same footer.");
                XDocument? footer = OoxmlPackage.Footer(document, section);
                Assert.NotNull(footer);
                Assert.Contains("PAGE", OoxmlPackage.FieldCodes(footer));
                Assert.Contains("NUMPAGES", OoxmlPackage.FieldCodes(footer));
                string footerText = string.Concat(footer.Descendants(OoxmlPackage.WordprocessingMl + "t").Select(text => text.Value));
                Assert.Contains("Page", footerText, StringComparison.Ordinal);
                Assert.Contains("of", footerText, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ConvertToWord_KeepsAnExcelIndent_At14Point4PointsALevel()
    {
        byte[] document = Converter.ConvertToWord(EditableWorkbook);

        XElement indented = OoxmlPackage.WordParagraphs(document).Single(paragraph => OoxmlPackage.ParagraphText(paragraph) == "Indented label");
        XElement? indent = indented.Element(OoxmlPackage.WordprocessingMl + "pPr")?.Element(OoxmlPackage.WordprocessingMl + "ind");
        Assert.Equal(2 * INDENT_LEVEL_TWIPS, OoxmlPackage.Twips(indent, "left") ?? OoxmlPackage.Twips(indent, "start"));
    }

    [Fact]
    public void ConvertToWord_TurnsAMarkdownListInACellIntoRoundBulletsAtTheMargin()
    {
        var file = new WordFile(Converter.ConvertToWord(EditableWorkbook));

        foreach (string item in new[] { "First point", "Second point" })
        {
            XElement paragraph = file.Paragraphs.Single(candidate => WordFile.TextOf(candidate) == item);
            Assert.Equal(ROUND_BULLET, file.ListMarker(paragraph));
            Assert.Equal(INDENT_LEVEL_TWIPS, file.ListTextPosition(paragraph));
            Assert.Equal(0, file.MarkerPosition(paragraph));
        }
    }

    [Fact]
    public void ConvertToWord_FitsTablesInsideTheMargins_WithRowsThatGrowToTheirText()
    {
        byte[] document = Converter.ConvertToWord(CreditReviewTemplate);
        XElement section = OoxmlPackage.Sections(document)[0];
        int contentWidth = OoxmlPackage.Twips(section.Element(OoxmlPackage.WordprocessingMl + "pgSz"), "w")!.Value - (2 * ONE_INCH_TWIPS);

        XElement firstTable = OoxmlPackage.Part(document, OoxmlPackage.DOCUMENT_PART).Descendants(OoxmlPackage.WordprocessingMl + "tbl").First();
        int gridWidth = GridWidth(firstTable);
        Assert.True(gridWidth <= contentWidth, $"The table is {gridWidth} twips wide; the margins leave {contentWidth}.");
        Assert.DoesNotContain(firstTable.Descendants(OoxmlPackage.WordprocessingMl + "trHeight"), IsExactHeight);
    }

    [Fact]
    public void ConvertToWord_KeepsTheScaleAndRowHeightsOfASheetSetUpForPrint()
    {
        byte[] document = Converter.ConvertToWord(ContractFixture.Bytes(ContractFixture.PRINT_SCALED_WORKBOOK));

        List<XElement> tables = [.. OoxmlPackage.Part(document, OoxmlPackage.DOCUMENT_PART).Descendants(OoxmlPackage.WordprocessingMl + "tbl")];
        Assert.Equal(2, tables.Count);
        (XElement scaled, XElement plain) = (tables[0], tables[1]);
        Assert.InRange((double)GridWidth(scaled) / GridWidth(plain), PRINT_SCALE - PRINT_SCALE_TOLERANCE, PRINT_SCALE + PRINT_SCALE_TOLERANCE);
        Assert.All(scaled.Elements(OoxmlPackage.WordprocessingMl + "tr"), row => Assert.Contains(row.Descendants(OoxmlPackage.WordprocessingMl + "trHeight"), IsExactHeight));
        Assert.DoesNotContain(plain.Descendants(OoxmlPackage.WordprocessingMl + "trHeight"), IsExactHeight);
    }

    [Fact]
    public void ConvertToWord_OfAWorkbookWithoutAVisibleSheet_Fails()
    {
        IWorkbookToWordConverter converter = Converter;
        byte[] workbook = ContractFixture.Bytes(ContractFixture.NO_VISIBLE_SHEET_WORKBOOK);

        _ = Assert.Throws<InvalidOperationException>(() => converter.ConvertToWord(workbook));
    }

    private static int GridWidth(XElement table)
    {
        return WordFile.GridColumns(table).Sum();
    }

    private static bool IsExactHeight(XElement height)
    {
        return (string?)height.Attribute(OoxmlPackage.WordprocessingMl + "hRule") == "exact";
    }
}
