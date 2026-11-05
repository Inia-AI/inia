using IniaAi.Common.Text;

namespace IniaAi.Common.Tests;

public class MarkdownHelpersTests
{
    [Fact]
    public void FormatMarkdownTable_ValidInput_ReturnsFormattedTable()
    {
        // Arrange
        string input =
        """
        | Header1 | Header2 |
        |---|---|
        |Cell1|Cell2|
        """;
        string expectedOutput =
        """
        |Header1|Header2|
        |-------|-------|
        |Cell1  |Cell2  |
        """;

        // Act
        string result = MarkdownFormatter.FormatMarkdownTable(input);

        // Assert
        Assert.Equal(expectedOutput, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void FormatMarkdownTable_InputWithExtraPipes_ReturnsFormattedTable()
    {
        // Arrange
        string input =
        """
        || Header1 || Header2 ||
        |---|---|---|---|---|
        || Cell1   || Cell2   ||
        """;
        string expectedOutput =
        """
        |   |Header1|   |Header2|   |
        |---|-------|---|-------|---|
        |   |Cell1  |   |Cell2  |   |
        """;

        // Act
        string result = MarkdownFormatter.FormatMarkdownTable(input);

        // Assert
        Assert.Equal(expectedOutput, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void FormatMarkdownTable_EmptyInput_ReturnsEmptyString()
    {
        // Arrange
        string input = "";
        string expectedOutput = "";

        // Act
        string result = MarkdownFormatter.FormatMarkdownTable(input);

        // Assert
        Assert.Equal(expectedOutput, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void FormatMarkdownTable_InvalidInput_ReturnsOriginalInput()
    {
        // Arrange
        string input =
        """
        |Invalid|
        |Markdown|Table|
        """;
        string expectedOutput =
        """
        |Invalid|
        |Markdown|Table|
        """;

        // Act
        string result = MarkdownFormatter.FormatMarkdownTable(input);

        // Assert
        Assert.Equal(expectedOutput, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void FormatMarkdownTable_InputWithDifferentLineEndings_ReturnsFormattedTable()
    {
        // Arrange
        string input = "| Header1 | Header2 |\r\n|---|---|\r\n|Cell1|Cell2|\r\n";
        string expectedOutput =
        """
        |Header1|Header2|
        |-------|-------|
        |Cell1  |Cell2  |
        """;

        // Act
        string result = MarkdownFormatter.FormatMarkdownTable(input);

        // Assert
        Assert.Equal(expectedOutput, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void FormatMarkdownTable_InputWithEmptyCells_ReturnsFormattedTable()
    {
        // Arrange
        string input =
        """
        | Header1 | Header2 |
        |---|---|
        |Cell1| |
        """;
        string expectedOutput =
        """
        |Header1|Header2|
        |-------|-------|
        |Cell1  |       |
        """;

        // Act
        string result = MarkdownFormatter.FormatMarkdownTable(input);

        // Assert
        Assert.Equal(expectedOutput, result, ignoreLineEndingDifferences: true);
    }
}
