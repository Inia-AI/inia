using IniaAi.AiConnection.Anthropic.ApiModel;
using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.Common.Enums;

namespace IniaAi.AiConnection.Tests.Anthropic.ApiModel;

public class SourceTests
{
    private const string JSON =
    """
    {
      "type": "base64",
      "media_type": "image/jpeg",
      "data": "base64_encoded_data_here..."
    }
    """;

    [Fact]
    public void FromJson_ValidJson_ReturnsSource()
    {
        // Arrange

        // Act
        var source = Source.FromJson(JSON);

        // Assert
        Assert.NotNull(source);
        Assert.Equal(SourceType.Base64, source.Type);
        Assert.Equal(MediaType.ImageJpeg, source.MediaType);
        Assert.Equal("base64_encoded_data_here...", source.Data);
    }

    [Fact]
    public void ToJson_ValidSource_ReturnsJson()
    {
        // Arrange
        var source = new Source
        {
            Type = SourceType.Base64,
            MediaType = MediaType.ImageJpeg,
            Data = "base64_encoded_data_here..."
        };

        // Act
        string json = source.ToJson(pretty: true);

        // Assert
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        Assert.Equal(JSON.Replace("\r\n", "\n"), json);
    }
}
