using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class ContentResponseTests
{
    [Fact]
    public void FromJson_TextWithCitations_ParsesCitationsCorrectly()
    {
        // Arrange
        const string json =
        """
        {
          "type": "text",
          "text": "According to the document, the answer is 42.",
          "citations": [
            {
              "type": "char_location",
              "cited_text": "the answer is 42",
              "document_index": 0,
              "document_title": "Page 1",
              "start_char_index": 100,
              "end_char_index": 116
            }
          ]
        }
        """;

        // Act
        var content = ContentResponse.FromJson(json);

        // Assert
        Assert.NotNull(content);
        Assert.Equal(ContentType.Text, content.Type);
        Assert.Equal("According to the document, the answer is 42.", content.Text);
        Assert.NotNull(content.Citations);
        _ = Assert.Single(content.Citations);

        Citation citation = content.Citations[0];
        Assert.Equal(CitationType.CharLocation, citation.Type);
        Assert.Equal("the answer is 42", citation.CitedText);
        Assert.Equal(0, citation.DocumentIndex);
        Assert.Equal("Page 1", citation.DocumentTitle);
        Assert.Equal(100, citation.StartCharIndex);
        Assert.Equal(116, citation.EndCharIndex);
    }

    [Fact]
    public void FromJson_TextWithPageLocationCitation_ParsesCorrectly()
    {
        // Arrange
        const string json =
        """
        {
          "type": "text",
          "text": "The revenue increased by 20%.",
          "citations": [
            {
              "type": "page_location",
              "cited_text": "revenue increased by 20%",
              "document_index": 1,
              "document_title": "Financial Report",
              "start_page_number": 5,
              "end_page_number": 6
            }
          ]
        }
        """;

        // Act
        var content = ContentResponse.FromJson(json);

        // Assert
        Assert.NotNull(content);
        Assert.NotNull(content.Citations);
        _ = Assert.Single(content.Citations);

        Citation citation = content.Citations[0];
        Assert.Equal(CitationType.PageLocation, citation.Type);
        Assert.Equal("revenue increased by 20%", citation.CitedText);
        Assert.Equal(1, citation.DocumentIndex);
        Assert.Equal("Financial Report", citation.DocumentTitle);
        Assert.Equal(5, citation.StartPageNumber);
        Assert.Equal(6, citation.EndPageNumber);
    }

    [Fact]
    public void FromJson_TextWithContentBlockCitation_ParsesCorrectly()
    {
        // Arrange
        const string json =
        """
        {
          "type": "text",
          "text": "The data shows a trend.",
          "citations": [
            {
              "type": "content_block_location",
              "cited_text": "data shows a trend",
              "document_index": 0,
              "start_block_index": 2,
              "end_block_index": 3
            }
          ]
        }
        """;

        // Act
        var content = ContentResponse.FromJson(json);

        // Assert
        Assert.NotNull(content);
        Assert.NotNull(content.Citations);
        _ = Assert.Single(content.Citations);

        Citation citation = content.Citations[0];
        Assert.Equal(CitationType.ContentBlockLocation, citation.Type);
        Assert.Equal("data shows a trend", citation.CitedText);
        Assert.Equal(0, citation.DocumentIndex);
        Assert.Equal(2, citation.StartBlockIndex);
        Assert.Equal(3, citation.EndBlockIndex);
    }

    [Fact]
    public void FromJson_TextWithMultipleCitations_ParsesAll()
    {
        // Arrange
        const string json =
        """
        {
          "type": "text",
          "text": "The report states A and B.",
          "citations": [
            {
              "type": "char_location",
              "cited_text": "A",
              "document_index": 0,
              "start_char_index": 10,
              "end_char_index": 11
            },
            {
              "type": "char_location",
              "cited_text": "B",
              "document_index": 1,
              "start_char_index": 20,
              "end_char_index": 21
            }
          ]
        }
        """;

        // Act
        var content = ContentResponse.FromJson(json);

        // Assert
        Assert.NotNull(content);
        Assert.NotNull(content.Citations);
        Assert.Equal(2, content.Citations.Count);
        Assert.Equal("A", content.Citations[0].CitedText);
        Assert.Equal("B", content.Citations[1].CitedText);
        Assert.Equal(0, content.Citations[0].DocumentIndex);
        Assert.Equal(1, content.Citations[1].DocumentIndex);
    }

    [Fact]
    public void FromJson_TextWithoutCitations_CitationsIsNull()
    {
        // Arrange
        const string json =
        """
        {
          "type": "text",
          "text": "Just a regular response without citations."
        }
        """;

        // Act
        var content = ContentResponse.FromJson(json);

        // Assert
        Assert.NotNull(content);
        Assert.Equal(ContentType.Text, content.Type);
        Assert.Equal("Just a regular response without citations.", content.Text);
        Assert.Null(content.Citations);
    }

    [Fact]
    public void FromJson_ThinkingContent_ParsesCorrectly()
    {
        // Arrange
        const string json =
        """
        {
          "type": "thinking",
          "thinking": "Let me analyze this step by step...",
          "signature": "abc123signature"
        }
        """;

        // Act
        var content = ContentResponse.FromJson(json);

        // Assert
        Assert.NotNull(content);
        Assert.Equal(ContentType.Thinking, content.Type);
        Assert.Equal("Let me analyze this step by step...", content.Thinking);
        Assert.Equal("abc123signature", content.ThinkingSignature);
    }
}
