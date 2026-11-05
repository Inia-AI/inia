using System.Text.RegularExpressions;

namespace IniaAi.Common.Tests;

/// <summary>
/// Tests for the PageFormatting utility class
/// </summary>
public class PageFormattingTests
{
    [Fact]
    public void FormatPageTag_ProducesExpectedFormat()
    {
        // Arrange
        int pageNumber = 42;
        string documentName = "Test-Document";
        string content = "This is test content";

        // Act
        string formattedTag = PageFormatting.FormatPageTag(pageNumber, documentName, content);

        // Assert
        string expected = "<page42(Test-Document)>\nThis is test content\n</page42(Test-Document)>";
        Assert.Equal(expected, formattedTag);
    }

    [Fact]
    public void CreatePageTagRegex_CanMatchFormatPageTagOutput()
    {
        // Arrange
        int pageNumber = 10;
        string documentName = "My-Test-File";
        string content = "Page content here";

        string formattedTag = PageFormatting.FormatPageTag(pageNumber, documentName, content);
        Regex regex = PageFormatting.CreatePageTagRegex();

        // Act
        MatchCollection matches = regex.Matches(formattedTag);

        // Assert
        _ = Assert.Single(matches);
        Assert.Equal("10", matches[0].Groups[1].Value); // Page number
        Assert.Equal("My-Test-File", matches[0].Groups[2].Value); // Document name
        Assert.Contains("Page content here", matches[0].Groups[3].Value); // Content
    }

    [Fact]
    public void SanitizeDocumentName_ReplacesSpacesWithDashes()
    {
        // Arrange
        string originalName = "Document With Spaces";

        // Act
        string sanitizedName = PageFormatting.SanitizeDocumentName(originalName);

        // Assert
        Assert.Equal("Document-With-Spaces", sanitizedName);
    }

    [Fact]
    public void GetPageTagPattern_ReturnsValidRegexPattern()
    {
        // Arrange & Act
        string pattern = PageFormatting.GetPageTagPattern();

        // Assert - Should be able to create a regex with this pattern
        var regex = new Regex(pattern);
        Assert.NotNull(regex);
    }

    [Fact]
    public void RegexPattern_IsTrulyDerivedFromFormatMethod()
    {
        // This test ensures the regex pattern is truly unified with the format method

        // Arrange - Create a formatted page tag using the format method
        int pageNumber = 123;
        string documentName = "Sample-Document";
        string content = "Page content with\nmultiple lines\nand special chars!";

        string formattedOutput = PageFormatting.FormatPageTag(pageNumber, documentName, content);

        // Act - Use the regex pattern to parse the formatted output
        string pattern = PageFormatting.GetPageTagPattern();
        var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        MatchCollection matches = regex.Matches(formattedOutput);

        // Assert - The regex must be able to parse what the format method produces
        _ = Assert.Single(matches);
        Assert.Equal("123", matches[0].Groups[1].Value); // Page number
        Assert.Equal("Sample-Document", matches[0].Groups[2].Value); // Document name
        Assert.Equal(content, matches[0].Groups[3].Value); // Content (should match exactly)

        // Verify the full match covers the entire formatted output
        Assert.Equal(formattedOutput, matches[0].Value);
    }

    [Fact]
    public void CreatePageTagRegex_MatchesMultiplePages()
    {
        // Arrange - Simulate multiple page tags as would be produced by Page entity methods
        string simulatedPageOutput = PageFormatting.FormatPageTag(1, "Test-Document", "First page content") +
                                   "\n\n" + PageFormatting.FormatPageTag(2, "Test-Document", "Second page content") +
                                   "\n\n" + PageFormatting.FormatPageTag(3, "Another-File", "Third page content");

        // Act - Test that the regex can parse this multi-page output
        Regex regex = PageFormatting.CreatePageTagRegex();
        MatchCollection matches = regex.Matches(simulatedPageOutput);

        // Assert
        Assert.Equal(3, matches.Count);

        // Verify first page
        Assert.Equal("1", matches[0].Groups[1].Value);
        Assert.Equal("Test-Document", matches[0].Groups[2].Value);
        Assert.Contains("First page content", matches[0].Groups[3].Value);

        // Verify second page
        Assert.Equal("2", matches[1].Groups[1].Value);
        Assert.Equal("Test-Document", matches[1].Groups[2].Value);
        Assert.Contains("Second page content", matches[1].Groups[3].Value);

        // Verify third page
        Assert.Equal("3", matches[2].Groups[1].Value);
        Assert.Equal("Another-File", matches[2].Groups[2].Value);
        Assert.Contains("Third page content", matches[2].Groups[3].Value);
    }

    [Fact]
    public void CreatePageName_ProducesExpectedOutput()
    {
        // Arrange
        int pageNumber = 7;
        string documentName = "My-Document";

        // Act
        string pageName = PageFormatting.FormatPageName(pageNumber, documentName);

        // Assert
        Assert.Equal("page7(My-Document)", pageName);
    }

    [Fact]
    public void CreatePageName_ProducesExpectedOutput_WithSpacesInDocumentName()
    {
        // Arrange
        int pageNumber = 15;
        string documentName = "Document With Spaces";

        // Act
        string sanitizedDocumentName = PageFormatting.SanitizeDocumentName(documentName);
        string pageName = PageFormatting.FormatPageName(pageNumber, sanitizedDocumentName);

        // Assert
        Assert.Equal("page15(Document-With-Spaces)", pageName);
    }

    [Fact]
    public void CreatePageTagRegex_MatchesSimplePageTags()
    {
        // Verify that the regex matches simple page tag formats
        string testInput = "<page5(test-file.pdf)>content</page5(test-file.pdf)>";
        Regex regex = PageFormatting.CreatePageTagRegex();

        // Act
        MatchCollection matches = regex.Matches(testInput);

        // Assert
        _ = Assert.Single(matches);
        Assert.Equal(testInput, matches[0].Value);
    }

    [Theory]
    [InlineData("<page2(northwind-factsheet.pdf)-text>\nPrincipals\n\nAlex Rivera", "Principals\n\nAlex Rivera")]
    [InlineData("Managing Partner since 1 May 2011.\n</page2(northwind-factsheet.pdf)-text>", "Managing Partner since 1 May 2011.")]
    [InlineData("<page1(Report-(final).pdf)-text>\nNet assets 4,027", "Net assets 4,027")]
    [InlineData("<page2(exa-search-results.txt)-text>\nSource URL: https://example.org/\n\nExample Corp", "Example Corp")]
    public void RemovePageMarkers_StripsTheWrapperThePromptAddsAroundAPage(string citedText, string expected)
    {
        Assert.Equal(expected, PageFormatting.RemovePageMarkers(citedText));
    }

    [Theory]
    [InlineData("Revenue grew on page 2 of the report")]
    [InlineData("See <b>page2</b> for details")]
    [InlineData("")]
    public void RemovePageMarkers_LeavesTextWithoutAMarkerAsItIs(string citedText)
    {
        Assert.Equal(citedText, PageFormatting.RemovePageMarkers(citedText));
    }

    [Fact]
    public void FormatPageText_WrapsThePageTextInItsMarkers()
    {
        Assert.Equal(
            "<page2(fund.pdf)-text>\nNet assets 4,027\n</page2(fund.pdf)-text>",
            PageFormatting.FormatPageText("page2(fund.pdf)", "Net assets 4,027", null));
    }

    [Fact]
    public void FormatPageText_WebPage_PutsItsSourceUrlLineFirst()
    {
        Assert.Equal(
            "<page1(results.txt)-text>\nSource URL: https://example.org/\n\nExample Corp\n</page1(results.txt)-text>",
            PageFormatting.FormatPageText("page1(results.txt)", "Example Corp", "https://example.org/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.org/")]
    public void RemovePageMarkers_OnAFormattedPage_GivesBackThePageText(string? sourceUrl)
    {
        string formatted = PageFormatting.FormatPageText("page3(fund.pdf)", "Alex Rivera, Partner since 1 May 2011", sourceUrl);

        Assert.Equal("Alex Rivera, Partner since 1 May 2011", PageFormatting.RemovePageMarkers(formatted));
    }
}
