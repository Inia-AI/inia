using IniaAi.FileFormats.Pdf;
using IniaAi.Testing.Documents;
using IniaAi.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.FileFormats.Tests.Pdf;

public class PdfExtractorTests
{
    private readonly ITestOutputHelper _output;

    public PdfExtractorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ExtractPageTextsSkippingFailedPages_ReadsTheTextLayer()
    {
        // Arrange
        BinaryData pdfBytes = new(File.ReadAllBytes(Path.Combine("TestResources", "SimpleTestSearchableWithTextOnImage.pdf")));
        using IPdfDocument pdf = FileFormatsUnderTest.Pdf.Open(pdfBytes.ToStream(), "SimpleTestSearchableWithTextOnImage.pdf");

        // Act
        IReadOnlyList<string?> pageTexts = PdfExtractor.ExtractPageTextsSkippingFailedPages(pdf, int.MaxValue, NullLogger.Instance);

        // Assert
        Assert.Contains(pageTexts, text => text?.Contains("Inia-Ai", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ExtractPageTextsSkippingFailedPages_StopsAfterTheLastPageAskedFor()
    {
        using IPdfDocument pdf = FileFormatsUnderTest.Pdf.Open(new MemoryStream(PdfFixtures.Pages("alpha page", "bravo page", "charlie page")), "pages.pdf");

        IReadOnlyList<string?> pageTexts = PdfExtractor.ExtractPageTextsSkippingFailedPages(pdf, maxPages: 2, NullLogger.Instance);

        Assert.Equal(2, pageTexts.Count);
        Assert.Contains("alpha page", pageTexts[0]);
        Assert.Contains("bravo page", pageTexts[1]);
    }

    [Fact]
    public void ExtractPageTextsSkippingFailedPages_GivesNoTextForAPageThatDoesNotParse_AndReadsTheRest()
    {
        using IPdfDocument pdf = FileFormatsUnderTest.Pdf.Open(new MemoryStream(PdfFixtures.WithUnresolvablePage(2, "one", "two", "three")), "unresolvable.pdf");

        IReadOnlyList<string?> pageTexts = PdfExtractor.ExtractPageTextsSkippingFailedPages(pdf, maxPages: 3, NullLogger.Instance);

        Assert.Contains("one", pageTexts[0]);
        Assert.Null(pageTexts[1]);
        Assert.Contains("three", pageTexts[2]);
    }

    [Fact]
    public void ExtractPaginatedText_WhenNormalPortraitDocument_ExtractsAllPagesAsSinglePages()
    {
        // Arrange
        using IPdfDocument pdf = FileFormatsUnderTest.Pdf.Open(new MemoryStream(PdfFixtures.Pages("cover", "contents", "", "results", "notes")), "portrait.pdf");
        PdfExtractor extractor = new(TestsLogger.CreateLogger<PdfExtractor>(_output));
        int expectedPageCount = pdf.PageCount;
        IEnumerable<int> allPageNumbers = Enumerable.Range(1, expectedPageCount);

        // Act
        List<PdfExtractionPage> extractedPages = extractor.ExtractPaginatedText(pdf, allPageNumbers);

        // Assert
        Assert.Equal(expectedPageCount, extractedPages.Count);

        // Verify all pages are detected as single pages (IsLeftSide is null)
        Assert.All(extractedPages, page => Assert.Null(page.IsLeftSide));

        // Verify DocumentPageNumbers are sequential
        for (int i = 0; i < extractedPages.Count; i++)
        {
            Assert.Equal(i + 1, extractedPages[i].FilePageNumber);
        }

        // Verify that at least some pages have text content (some PDFs have blank pages)
        Assert.True(extractedPages.Count(p => !string.IsNullOrWhiteSpace(p.Text)) == 4,
            "Expected 4 out 5 pages to have text content");
    }

    [Fact]
    public void ExtractPaginatedText_WhenExtractingEvenPagesOnly_ExtractsAllAsSinglePages()
    {
        // Arrange
        BinaryData pdfBytes = new(File.ReadAllBytes(Path.Combine("TestResources", "Topics_10_Pages.pdf")));
        using IPdfDocument pdf = FileFormatsUnderTest.Pdf.Open(pdfBytes.ToStream(), "Topics_10_Pages.pdf");
        PdfExtractor extractor = new(TestsLogger.CreateLogger<PdfExtractor>(_output));

        // Extract only even pages: 2, 4, 6, 8, 10
        IEnumerable<int> evenPageNumbers = new[] { 2, 4, 6, 8, 10 };

        // Act
        List<PdfExtractionPage> extractedPages = extractor.ExtractPaginatedText(pdf, evenPageNumbers);

        // Assert
        Assert.Equal(5, extractedPages.Count);

        // Verify all pages are detected as single pages (IsLeftSide is null)
        Assert.All(extractedPages, page => Assert.Null(page.IsLeftSide));

        // Verify DocumentPageNumbers match the even pages we requested
        Assert.Equal(2, extractedPages[0].FilePageNumber);
        Assert.Equal(4, extractedPages[1].FilePageNumber);
        Assert.Equal(6, extractedPages[2].FilePageNumber);
        Assert.Equal(8, extractedPages[3].FilePageNumber);
        Assert.Equal(10, extractedPages[4].FilePageNumber);

        // Verify all pages have text content
        Assert.All(extractedPages, page => Assert.False(string.IsNullOrWhiteSpace(page.Text)));

        // Verify first word of each page to ensure correct extraction
        string[] expectedFirstWords = ["Helicopters", "Bears", "Dogs", "Motorcycles", "Cats"];

        for (int i = 0; i < extractedPages.Count; i++)
        {
            string firstWord = extractedPages[i].Text
                .Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";

            Assert.Equal(expectedFirstWords[i], firstWord);
        }
    }

    [Fact]
    public void ExtractPaginatedText_WhenLandscapeSinglePageDocument_ExtractsAllAsSinglePages()
    {
        // Arrange
        BinaryData pdfBytes = new(File.ReadAllBytes(Path.Combine("TestResources", "landscape.pdf")));
        using IPdfDocument pdf = FileFormatsUnderTest.Pdf.Open(pdfBytes.ToStream(), "landscape.pdf");
        PdfExtractor extractor = new(TestsLogger.CreateLogger<PdfExtractor>(_output));
        int expectedPageCount = pdf.PageCount;
        IEnumerable<int> allPageNumbers = Enumerable.Range(1, expectedPageCount);

        // Act
        List<PdfExtractionPage> extractedPages = extractor.ExtractPaginatedText(pdf, allPageNumbers);

        // Assert
        Assert.Equal(expectedPageCount, extractedPages.Count);

        // Verify all pages are detected as single pages (IsLeftSide is null)
        Assert.All(extractedPages, page => Assert.Null(page.IsLeftSide));

        // Verify DocumentPageNumbers are sequential
        for (int i = 0; i < extractedPages.Count; i++)
        {
            Assert.Equal(i + 1, extractedPages[i].FilePageNumber);
        }

        // Verify first word of each page to ensure correct extraction
        string[] expectedFirstWords = ["Overview", "Features", "Contact"];

        for (int i = 0; i < extractedPages.Count; i++)
        {
            string firstWord = extractedPages[i].Text
                .Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";

            Assert.Equal(expectedFirstWords[i], firstWord);
        }
    }

    [Fact]
    public void ExtractPaginatedText_WhenDocumentHasDoublePage_SplitsDoublePageCorrectly()
    {
        // Arrange
        using IPdfDocument pdf = FileFormatsUnderTest.Pdf.Open(new MemoryStream(LayoutSamples.DoublePages()), "double_pages.pdf");
        PdfExtractor extractor = new(TestsLogger.CreateLogger<PdfExtractor>(_output));
        int pdfPageCount = pdf.PageCount;
        IEnumerable<int> allPageNumbers = Enumerable.Range(1, pdfPageCount);

        // Act
        List<PdfExtractionPage> extractedPages = extractor.ExtractPaginatedText(pdf, allPageNumbers);

        // Assert - PDF has 3 physical pages, all pages are double pages, but page 3 has only left side content
        Assert.Equal(5, extractedPages.Count);

        // Page 1 should be split into two pages (left and right)
        Assert.Equal(1, extractedPages[0].FilePageNumber);
        Assert.True(extractedPages[0].IsLeftSide);
        Assert.StartsWith("Lorem", extractedPages[0].Text.Trim());

        Assert.Equal(1, extractedPages[1].FilePageNumber);
        Assert.False(extractedPages[1].IsLeftSide);
        Assert.StartsWith("In", extractedPages[1].Text.Trim());

        // Page 2 should be split into two pages (left and right)
        Assert.Equal(2, extractedPages[2].FilePageNumber);
        Assert.True(extractedPages[2].IsLeftSide);
        Assert.StartsWith("ullamcorper", extractedPages[2].Text.Trim());

        Assert.Equal(2, extractedPages[3].FilePageNumber);
        Assert.False(extractedPages[3].IsLeftSide);
        Assert.StartsWith("quis", extractedPages[3].Text.Trim());

        // Page 3 should have only left side content
        Assert.Equal(3, extractedPages[4].FilePageNumber);
        Assert.Null(extractedPages[4].IsLeftSide);
        Assert.StartsWith("Cras", extractedPages[4].Text.Trim());
    }
}
