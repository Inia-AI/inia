using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;

namespace IniaAi.AiConnection.Tests.Common.ChatConnector;

public class ResponseContentSequenceTests
{
    [Fact]
    public void Add_ResponseCommonContent_AddsContentToSequence()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        var content = new ResponseCommonContent("Hello, world!");

        // Act
        sequence.Add(content);

        // Assert
        _ = Assert.Single(sequence);
        Assert.Equal("Hello, world!", sequence[0].TextContent);
    }

    [Fact]
    public void Indexer_ReturnsCorrectContent()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        sequence.Add(new ResponseCommonContent("First"));
        sequence.Add(new ResponseCommonContent("Second"));

        // Act & Assert
        Assert.Equal("First", sequence[0].TextContent);
        Assert.Equal("Second", sequence[1].TextContent);
    }

    [Fact]
    public void Replace_TextContent_ReplacesStringInTextContent()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        sequence.Add(new ResponseCommonContent("Hello, {Name}!"));

        // Act
        sequence.Replace("{Name}", "John");

        // Assert
        Assert.Equal("Hello, John!", sequence[0].TextContent);
    }

    [Fact]
    public void Replace_SkipsNonTextContent()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        sequence.Add(new ResponseCommonContent("Replace {this}"));
        sequence.Add(new ResponseCommonContent(CommonContentType.Image));
        sequence.Add(new ResponseCommonContent("Also {this}"));

        // Act
        sequence.Replace("{this}", "done");

        // Assert
        Assert.Equal("Replace done", sequence[0].TextContent);
        Assert.Equal(CommonContentType.Image, sequence[1].Type);
        Assert.Equal("Also done", sequence[2].TextContent);
    }

    [Fact]
    public void Replace_HandlesNullTextContent()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        var content = new ResponseCommonContent(CommonContentType.Text) { TextContent = null };
        sequence.Add(content);

        // Act - should not throw
        sequence.Replace("anything", "replacement");

        // Assert
        Assert.Null(sequence[0].TextContent);
    }

    [Fact]
    public void ToString_TextContent_ReturnsStringContent()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        sequence.Add(new ResponseCommonContent("Some text"));

        // Act
        string result = sequence.ToString();

        // Assert
        Assert.Equal("StringContent", result);
    }

    [Fact]
    public void ToString_ImageContent_ReturnsImageContent()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        sequence.Add(new ResponseCommonContent(CommonContentType.Image));

        // Act
        string result = sequence.ToString();

        // Assert
        Assert.Equal("ImageContent", result);
    }

    [Fact]
    public void ToString_FileContent_ReturnsFileContent()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        sequence.Add(new ResponseCommonContent(CommonContentType.File));

        // Act
        string result = sequence.ToString();

        // Assert
        Assert.Equal("FileContent", result);
    }

    [Fact]
    public void ToString_MixedContent_ReturnsConcatenatedTypes()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        sequence.Add(new ResponseCommonContent("text"));
        sequence.Add(new ResponseCommonContent(CommonContentType.Image));
        sequence.Add(new ResponseCommonContent(CommonContentType.File));

        // Act
        string result = sequence.ToString();

        // Assert
        Assert.Equal("StringContent ImageContent FileContent", result);
    }

    [Fact]
    public void GetEnumerator_AllowsEnumeration()
    {
        // Arrange
        var sequence = new ResponseContentSequence();
        sequence.Add(new ResponseCommonContent("First"));
        sequence.Add(new ResponseCommonContent("Second"));

        // Act
        var items = sequence.ToList();

        // Assert
        Assert.Equal(2, items.Count);
        Assert.Equal("First", items[0].TextContent);
        Assert.Equal("Second", items[1].TextContent);
    }

    private static ResponseContentSequence WithStopReason(string? stopReason)
    {
        var response = new ResponseContentSequence();
        if (stopReason is not null)
        {
            response.Metadata[ResponseContentSequence.STOP_REASON_METADATA_KEY] = stopReason;
        }

        return response;
    }

    [Fact]
    public void IsTruncated_MaxTokensStopReason_ReturnsTrue()
    {
        Assert.True(WithStopReason(ResponseContentSequence.MAX_TOKENS_STOP_REASON).IsTruncated);
    }

    [Fact]
    public void IsTruncated_EndTurnStopReason_ReturnsFalse()
    {
        Assert.False(WithStopReason("EndTurn").IsTruncated);
    }

    [Fact]
    public void IsTruncated_NoStopReason_ReturnsFalse()
    {
        Assert.False(WithStopReason(null).IsTruncated);
    }
}
