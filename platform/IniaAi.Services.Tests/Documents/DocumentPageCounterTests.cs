using ClosedXML.Excel;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces;
using IniaAi.FileFormats.Pages;
using IniaAi.Testing.Documents;
using System.Text;

namespace IniaAi.Services.Tests.Documents;

public sealed class DocumentPageCounterTests
{
    private const MediaType WORD = MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument;
    private const MediaType WORKBOOK = MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet;

    private readonly IDocumentPageCounter _counter = FileFormatsUnderTest.PageCounter;

    [Fact]
    public void APdf_CountsItsPages()
    {
        Assert.Equal(3, _counter.Count(PdfFixtures.Pages("One", "Two", "Three"), MediaType.ApplicationPdf, "report.pdf"));
    }

    [Fact]
    public void APicture_CountsOnePage()
    {
        Assert.Equal(1, _counter.Count(File.ReadAllBytes("TestResources/ManagerQs1.png"), MediaType.ImagePng, "scan.png"));
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(TextPages.PAGE_CHARACTERS, 1)]
    [InlineData(TextPages.PAGE_CHARACTERS + 1, 2)]
    [InlineData(9_000, 3)]
    public void AText_CountsAPageForEveryPageOfCharacters(int characters, int pages)
    {
        Assert.Equal(pages, _counter.Count(Encoding.UTF8.GetBytes(new string('a', characters)), MediaType.TextPlain, "notes.txt"));
    }

    [Fact]
    public void AText_CountsThePagesProcessingMakes_WhenParagraphsLeavePagesPartFull()
    {
        string text = string.Join("\n\n", Enumerable.Repeat(new string('p', 2_100), 3));

        Assert.Equal(TextPages.Split(text, markdown: false).Count, _counter.Count(Encoding.UTF8.GetBytes(text), MediaType.TextPlain, "notes.txt"));
        Assert.Equal(3, _counter.Count(Encoding.UTF8.GetBytes(text), MediaType.TextPlain, "notes.txt"));
    }

    [Fact]
    public void AMarkdownFileOfOnePage_CountsOnePage_AsProcessingKeepsItWhole()
    {
        string markdown = "# Summary\n\n" + new string('s', 2_500) + "\n# Outlook\nStable.";

        Assert.Single(TextPages.Split(markdown, markdown: true));
        Assert.Equal(1, _counter.Count(Encoding.UTF8.GetBytes(markdown), MediaType.TextMarkdown, "notes.md"));
    }

    [Fact]
    public void ACsv_CountsThePagesProcessingMakes()
    {
        string csv = "Date,Account,Amount\n" + string.Join("\n", Enumerable.Range(1, 120).Select(row => $"2026-01-01,Account {row},{row}"));

        Assert.Equal(3, _counter.Count(Encoding.UTF8.GetBytes(csv), MediaType.TextCsv, "ledger.csv"));
    }

    [Fact]
    public void ALongCsv_CountsItsText_NotOnlyItsRowPages()
    {
        string wide = new('x', 300);
        string csv = "Name,Note\n" + string.Join("\n", Enumerable.Range(1, 100).Select(row => $"Row {row},{wide}"));

        int? pages = _counter.Count(Encoding.UTF8.GetBytes(csv), MediaType.TextCsv, "notes.csv");

        Assert.True(pages > 2, $"Two row pages hold 30,000 characters, so the count is {pages}, more than 2.");
    }

    [Fact]
    public void ACsvWithARowLongerThanTheReaderTakes_HasNoCount()
    {
        string csv = "Name,Amount\n" + new string(',', CsvPages.MAX_ROW_CHARACTERS + 1) + "\n";

        Assert.Null(_counter.Count(Encoding.UTF8.GetBytes(csv), MediaType.TextCsv, "commas.csv"));
    }

    [Fact]
    public void AWorkbook_CountsAPageForEveryFiftyRowsOfEachVisibleSheet()
    {
        byte[] workbook = OoxmlFixtures.BuiltWorkbook(book =>
        {
            IXLWorksheet visible = book.AddWorksheet("Ledger");
            IXLWorksheet hidden = book.AddWorksheet("Workings");
            for (int row = 1; row <= 121; row++)
            {
                visible.Cell(row, 1).Value = $"Account {row}";
                visible.Cell(row, 2).Value = row;
            }

            for (int row = 1; row <= 500; row++)
            {
                hidden.Cell(row, 1).Value = row;
            }

            hidden.Hide();
        });

        Assert.Equal(3, _counter.Count(workbook, WORKBOOK, "ledger.xlsx"));
    }

    [Fact]
    public void AWorkbookRepeatingOneLongText_CountsTheTextEveryCellShows()
    {
        string text = new('n', 10_000);
        byte[] workbook = OoxmlFixtures.BuiltWorkbook(book =>
        {
            IXLWorksheet sheet = book.AddWorksheet("Notes");
            for (int row = 1; row <= 100; row++)
            {
                sheet.Cell(row, 1).Value = text;
            }
        });

        Assert.True(_counter.Count(workbook, WORKBOOK, "notes.xlsx") >= 250);
    }

    [Fact]
    public void ALegacyWorkbook_CountsWhereTheBuildReadsIt_AndHasNoCountWhereTheBuildRefusesIt()
    {
        int? expected = FileFormatsUnderTest.ReadableFormats.Refusal(MediaType.ApplicationVndMsExcel) is null ? 1 : null;

        Assert.Equal(expected, _counter.Count(OoxmlFixtures.LegacyWorkbook(), MediaType.ApplicationVndMsExcel, "legacy.xls"));
    }

    [Fact]
    public void AWordFile_CountsItsTextInPages_AndEachDistinctPictureOnce()
    {
        byte[] text = WordFixtures.Document(new string('w', 9_000));
        byte[] pictures = WordFixtures.DocumentRepeatingOnePicture(File.ReadAllBytes("TestResources/ManagerQs1.png"), pages: 3);

        Assert.Equal(3, _counter.Count(text, WORD, "long.docx"));
        Assert.Equal(2, _counter.Count(pictures, WORD, "logos.docx"));
    }

    [Fact]
    public void AFileThatCannotBeRead_HasNoCount()
    {
        Assert.Null(_counter.Count(Encoding.UTF8.GetBytes("not a pdf"), MediaType.ApplicationPdf, "broken.pdf"));
    }
}
