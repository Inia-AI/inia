using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.AiConnection.Enums;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class MessageTests
{
    private const string JSON =
    """
    {
      "role": "user",
      "content": [
        {
          "type": "image",
          "source": {
            "type": "base64",
            "media_type": "image/jpeg",
            "data": "a123b45cdQ=="
          }
        },
        {
          "type": "text",
          "text": "What is in the above image?"
        }
      ]
    }
    """;

    [Fact]
    public void FromJson_ValidJson_ReturnsMessage()
    {
        // Arrange

        // Act
        var message = Message.FromJson(JSON);

        // Assert
        Assert.NotNull(message);
        Assert.Equal(MessageSenderRole.User, message.Role);
        Assert.Equal(2, message.Content.Count);
        Assert.Equal(ContentType.Image, message.Content[0].Type);
        Assert.Equal(ContentType.Text, message.Content[1].Type);
        Assert.Equal("What is in the above image?", message.Content[1].Text);
        Assert.NotNull(message.Content[0].Source);
        Assert.Equal(SourceType.Base64, message.Content[0].Source!.Type);
        Assert.Equal(MediaType.ImageJpeg, message.Content[0].Source!.MediaType);
        Assert.Equal("a123b45cdQ==", message.Content[0].Source!.Data);
    }

    [Fact]
    public void ToJson_ValidMessage_ReturnsJson()
    {
        // Arrange
        string base64Data = "a123b45cde==";

        var message = new Message(new ContentRequest(new(new BinaryContent(Convert.FromBase64String(base64Data), MediaType.ImageJpeg), CommonContentType.Image)));
        message.Content.Add(new(new("What is in the above image?")));

        // Act
        string json = message.ToJson(pretty: true);

        // Assert
        Assert.Equal(JSON.Replace("\r\n", "\n"), json);
    }
}
