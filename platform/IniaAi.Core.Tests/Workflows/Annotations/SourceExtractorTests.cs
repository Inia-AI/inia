using IniaAi.Core.Workflows.Annotations;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class SourceExtractorTests
{
    private readonly SourceExtractor _sourceExtractor;

    public SourceExtractorTests()
    {
        _sourceExtractor = new SourceExtractor();
    }

    [Fact]
    public void ExtractSources_WithEmptyResponse_ReturnsEmptyList()
    {
        // Arrange
        string rawResponse = "";

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractSources_WithNullResponse_ReturnsEmptyList()
    {
        // Arrange
        string? rawResponse = null;

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractSources_WithNoSourceTags_ReturnsEmptyList()
    {
        // Arrange
        string rawResponse = "This is a text without any source tags.";

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractSources_WithSingleSource_ReturnsSingleSource()
    {
        // Arrange
        string rawResponse = "Some text <source>Document1, page 1</source> more text";

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse);

        // Assert
        _ = Assert.Single(result);
        Assert.Equal("Document1, page 1", result[0]);
    }

    [Fact]
    public void ExtractSources_WithMultipleSources_ReturnsAllSources()
    {
        // Arrange
        string rawResponse = "Text before <source>Source1</source> middle text <source>Source2</source> text after";

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("Source1", result);
        Assert.Contains("Source2", result);
    }

    [Fact]
    public void ExtractSources_WithMultilineSources_HandlesNewlines()
    {
        // Arrange
        string rawResponse = "Text before <source>Source1\nSource2</source> text after";

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("Source1", result);
        Assert.Contains("Source2", result);
    }

    [Fact]
    public void ExtractSources_WithWindowsLineEndings_HandlesCarriageReturns()
    {
        // Arrange
        string rawResponse = "Text before <source>Source1\r\nSource2</source> text after";

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("Source1", result);
        Assert.Contains("Source2", result);
    }

    [Fact]
    public void ExtractSources_WithWhitespace_TrimsWhitespace()
    {
        // Arrange
        string rawResponse = "Text before <source>  Source1  \n  Source2  </source> text after";

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("Source1", result);
        Assert.Contains("Source2", result);
        Assert.All(result, source => Assert.Equal(source.Trim(), source));
    }

    [Fact]
    public void ExtractSources_WithEmptyLines_SkipsEmptyLines()
    {
        // Arrange
        string rawResponse = "Text before <source>Source1\n\n\nSource2</source> text after";

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("Source1", result);
        Assert.Contains("Source2", result);
    }

    [Fact]
    public void ExtractSources_WithMultipleSourceTags_CombinesAllSources()
    {
        // Arrange
        string rawResponse = "Text before <source>Source1</source> middle text <source>Source2</source> more text <source>Source3</source> text after";

        // Act
        List<string> result = _sourceExtractor.ExtractSources(rawResponse);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("Source1", result);
        Assert.Contains("Source2", result);
        Assert.Contains("Source3", result);
    }
}
