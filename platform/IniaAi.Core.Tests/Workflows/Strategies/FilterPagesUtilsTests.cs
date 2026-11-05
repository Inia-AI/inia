using IniaAi.Common;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// Simple unit tests for FilterPages utility functions that don't require fixtures
/// </summary>
public class FilterPagesUtilsTests
{
    [Theory]
    [InlineData("10, 40, 60-65", new[] { 10, 40, 60, 61, 62, 63, 64, 65 })]
    [InlineData("5", new[] { 5 })]
    [InlineData("1-3", new[] { 1, 2, 3 })]
    [InlineData("10, 15-20, 25", new[] { 10, 15, 16, 17, 18, 19, 20, 25 })]
    public void Utils_ParseStringForNumbersList_ReturnsExpectedNumbers(string input, int[] expected)
    {
        // Act
        List<int> result = Utils.ParseStringForNumbersList(input);

        // Assert
        Assert.Equal([.. expected], result);
    }

    [Fact]
    public void PageFormatting_CreatePageTagRegex_MatchesPageTags()
    {
        // Arrange
        string input = """
            <page10(filename.pdf)>
            Some text on page 10
            </page10(filename.pdf)>

            <page40(filename.pdf)>
            Some text on page 40
            </page40(filename.pdf)>

            <page65(otherfilename.pdf)>
            Some text on page 65
            </page65(otherfilename.pdf)>
            """;

        // Act
        Regex regex = PageFormatting.CreatePageTagRegex();
        MatchCollection matches = regex.Matches(input);

        // Assert
        Assert.Equal(3, matches.Count);

        // Check first match (page 10)
        Assert.Equal("10", matches[0].Groups[1].Value);
        Assert.Equal("filename.pdf", matches[0].Groups[2].Value);
        Assert.Contains("Some text on page 10", matches[0].Groups[3].Value);

        // Check second match (page 40)
        Assert.Equal("40", matches[1].Groups[1].Value);
        Assert.Equal("filename.pdf", matches[1].Groups[2].Value);
        Assert.Contains("Some text on page 40", matches[1].Groups[3].Value);

        // Check third match (page 65)
        Assert.Equal("65", matches[2].Groups[1].Value);
        Assert.Equal("otherfilename.pdf", matches[2].Groups[2].Value);
        Assert.Contains("Some text on page 65", matches[2].Groups[3].Value);
    }
}