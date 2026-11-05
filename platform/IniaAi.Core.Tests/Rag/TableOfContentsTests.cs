using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using static IniaAi.Core.Rag.Strategies.UseAutoDetectedTableOfContentsStrategy;

namespace IniaAi.Core.Tests.Rag;

public class TableOfContentsTests
{
    [Fact]
    public void FromString_WithValidInput_ShouldCreateTableOfContents()
    {
        // Arrange
        string input = """
            Introduction: 1
            Chapter 1: 5
            Chapter 2: 15
            Conclusion: 25
            """;

        // Act
        var toc = TableOfContents.FromString(input);

        // Assert
        Assert.NotNull(toc);
        Assert.Equal(4, toc.Sections.Count);
        Assert.Equal(1, toc.Sections["Introduction"]);
        Assert.Equal(5, toc.Sections["Chapter 1"]);
        Assert.Equal(15, toc.Sections["Chapter 2"]);
        Assert.Equal(25, toc.Sections["Conclusion"]);
    }

    [Fact]
    public void GetTextFromPages_WhenPageIsImageOnly_UsesOverview()
    {
        var doc = new Document(new FileMetadata("1234", "Report.pdf", 0, StorageProvider.Database, Guid.NewGuid().ToString()), MediaType.ApplicationPdf);
        var page = new Page(doc, 1, 1) { Overview = "Balance sheet overview" };
        page.Status = PageStatus.OCRNeedsReview;
        List<Page> pages = [page];

        string? result = GetTextFromPages(pages, [1]);

        Assert.NotNull(result);
        Assert.Contains("Balance sheet overview", result);
    }

    [Fact]
    public void FromString_WithNullOrWhitespace_ShouldReturnNull()
    {
        Assert.Null(TableOfContents.FromString(null));
        Assert.Null(TableOfContents.FromString(""));
        Assert.Null(TableOfContents.FromString(" "));
    }

    [Fact]
    public void GetPageNumbersForSections_WithValidSections_ShouldReturnCorrectPages()
    {
        // Arrange
        string input = """
            Chapter 1: 5
            Chapter 2: 10
            Chapter 3: 15
            Chapter 4: 25
            """;
        var toc = TableOfContents.FromString(input);
        string[] sections = ["Chapter 1", "Chapter 3"];

        // Act
        List<int>? pages = toc?.GetPageNumbersForSections(sections, 30);

        // Assert
        Assert.Equal([5, 6, 7, 8, 9, 10, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25], pages);
    }

    [Fact]
    public void GetPageNumbersForSections_WithLastSection_RequiresLastPage()
    {
        // Arrange
        string input = "Chapter 1: 5";
        var toc = TableOfContents.FromString(input);
        string[] sections = ["Chapter 1"];

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            toc?.GetPageNumbersForSections(sections));
    }
}
