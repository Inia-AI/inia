using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Enums;
using IniaAi.Configuration.Enums.Anthropic;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class RequestBodyTests
{
    private const string JSON1 =
    """
    {
      "messages": [
        {
          "role": "user",
          "content": [
            {
              "type": "text",
              "text": "Hello, Claude"
            }
          ]
        }
      ],
      "max_tokens": 1024,
      "model": "us.anthropic.claude-sonnet-4-6",
      "temperature": 0.2
    }
    """;

    private const string JSON2 =
    """
    {
      "messages": [
        {
          "role": "user",
          "content": [
            {
              "type": "text",
              "text": "Hello, Claude"
            }
          ]
        },
        {
          "role": "assistant",
          "content": [
            {
              "type": "text",
              "text": "Hello!"
            }
          ]
        },
        {
          "role": "user",
          "content": [
            {
              "type": "text",
              "text": "Can you describe LLMs to me?"
            }
          ]
        }
      ],
      "max_tokens": 1024,
      "model": "us.anthropic.claude-sonnet-4-6",
      "temperature": 0.2
    }
    """;

    private const string JSON3 =
    """
    {
      "messages": [
        {
          "role": "user",
          "content": [
            {
              "type": "text",
              "text": "What is latin for Ant? (A) Apoidea, (B) Rhopalocera, (C) Formicidae"
            }
          ]
        },
        {
          "role": "assistant",
          "content": [
            {
              "type": "text",
              "text": "The answer is ("
            }
          ]
        }
      ],
      "max_tokens": 1,
      "model": "us.anthropic.claude-sonnet-4-6",
      "temperature": 0.2
    }
    """;

    private const string JSON4 =
    """
    {
      "messages": [
        {
          "role": "user",
          "content": [
            {
              "type": "image",
              "source": {
                "type": "base64",
                "media_type": "image/jpeg",
                "data": "'$IMAGE_BASE64'"
              }
            },
            {
              "type": "text",
              "text": "What is in the above image?"
            }
          ]
        }
      ],
      "max_tokens": 1024,
      "model": "us.anthropic.claude-sonnet-4-6",
      "temperature": 0.2
    }
    """;

    [Fact]
    public void FromJson_ValidJson_ReturnsRequestBody1()
    {
        // Arrange & Act
        var requestBody = RequestBody.FromJson(JSON1);

        // Assert
        Assert.NotNull(requestBody);
        Assert.Equal(AiModel.UsAwsSonnet4_6, requestBody.Model);
        Assert.Equal(1024, requestBody.MaxTokens);
        _ = Assert.Single(requestBody.Messages);
        Assert.Equal(MessageSenderRole.User, requestBody.Messages[0].Role);
        _ = Assert.Single(requestBody.Messages[0].Content);
        Assert.Equal(ContentType.Text, requestBody.Messages[0].Content[0].Type);
        Assert.Equal("Hello, Claude", requestBody.Messages[0].Content[0].Text);
    }

    [Fact]
    public void FromJson_ValidJson_ReturnsRequestBody2()
    {
        // Arrange & Act
        var requestBody = RequestBody.FromJson(JSON2);

        // Assert
        Assert.NotNull(requestBody);
        Assert.Equal(AiModel.UsAwsSonnet4_6, requestBody.Model);
        Assert.Equal(1024, requestBody.MaxTokens);
        Assert.Equal(3, requestBody.Messages.Count);
        Assert.Equal(MessageSenderRole.User, requestBody.Messages[0].Role);
        _ = Assert.Single(requestBody.Messages[0].Content);
        Assert.Equal(ContentType.Text, requestBody.Messages[0].Content[0].Type);
        Assert.Equal("Hello, Claude", requestBody.Messages[0].Content[0].Text);
        Assert.Equal(MessageSenderRole.Assistant, requestBody.Messages[1].Role);
        _ = Assert.Single(requestBody.Messages[1].Content);
        Assert.Equal(ContentType.Text, requestBody.Messages[1].Content[0].Type);
        Assert.Equal("Hello!", requestBody.Messages[1].Content[0].Text);
        Assert.Equal(MessageSenderRole.User, requestBody.Messages[2].Role);
        _ = Assert.Single(requestBody.Messages[2].Content);
        Assert.Equal(ContentType.Text, requestBody.Messages[2].Content[0].Type);
        Assert.Equal("Can you describe LLMs to me?", requestBody.Messages[2].Content[0].Text);
    }

    [Fact]
    public void FromJson_ValidJson_ReturnsRequestBody3()
    {
        // Arrange & Act
        var requestBody = RequestBody.FromJson(JSON3);

        // Assert
        Assert.NotNull(requestBody);
        Assert.Equal(AiModel.UsAwsSonnet4_6, requestBody.Model);
        Assert.Equal(1, requestBody.MaxTokens);
        Assert.Equal(2, requestBody.Messages.Count);
        Assert.Equal(MessageSenderRole.User, requestBody.Messages[0].Role);
        _ = Assert.Single(requestBody.Messages[0].Content);
        Assert.Equal(ContentType.Text, requestBody.Messages[0].Content[0].Type);
        Assert.Equal("What is latin for Ant? (A) Apoidea, (B) Rhopalocera, (C) Formicidae", requestBody.Messages[0].Content[0].Text);
        Assert.Equal(MessageSenderRole.Assistant, requestBody.Messages[1].Role);
        _ = Assert.Single(requestBody.Messages[1].Content);
        Assert.Equal(ContentType.Text, requestBody.Messages[1].Content[0].Type);
        Assert.Equal("The answer is (", requestBody.Messages[1].Content[0].Text);
    }

    [Fact]
    public void FromJson_ValidJson_ReturnsRequestBody4()
    {
        // Arrange & Act
        var requestBody = RequestBody.FromJson(JSON4);

        // Assert
        Assert.NotNull(requestBody);
        Assert.Equal(AiModel.UsAwsSonnet4_6, requestBody.Model);
        Assert.Equal(1024, requestBody.MaxTokens);
        _ = Assert.Single(requestBody.Messages);
        Assert.Equal(MessageSenderRole.User, requestBody.Messages[0].Role);
        Assert.Equal(2, requestBody.Messages[0].Content.Count);
        Assert.Equal(ContentType.Image, requestBody.Messages[0].Content[0].Type);
        Assert.Equal(ContentType.Text, requestBody.Messages[0].Content[1].Type);
        Assert.Equal("What is in the above image?", requestBody.Messages[0].Content[1].Text);
    }

    [Theory]
    [InlineData(JSON1)]
    [InlineData(JSON2)]
    [InlineData(JSON3)]
    [InlineData(JSON4)]
    public void ToJson_ValidRRequestBody_ReturnsJson(string json)
    {
        // Arrange
        var requestBody = RequestBody.FromJson(json);

        // Act
        string result = requestBody!.ToJson(pretty: true);

        // Assert
        Assert.Equal(json.Replace("\r\n", "\n"), result);
    }

    [Fact]
    public void ToString_ValidRequestBody_ReturnsString()
    {
        // Arrange
        var requestBody = RequestBody.FromJson(JSON1);
        string expectedResult =
        """
        Model: UsAwsSonnet4_6, Max tokens: 1024, 1 messages.
        Message 0 content: User role, 1 text content, 0 image content
        """;

        // Act
        string result = requestBody!.ToString();

        // Assert
        Assert.Equal(expectedResult, result, ignoreCase: true, ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);
    }
}
