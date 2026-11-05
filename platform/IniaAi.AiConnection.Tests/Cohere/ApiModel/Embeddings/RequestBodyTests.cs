using IniaAi.AiConnection.Cohere.ApiModel.Embeddings;
using IniaAi.AiConnection.Cohere.ApiModel.Embeddings.Enums;
using IniaAi.Configuration.Enums.Cohere;

namespace IniaAi.AiConnection.Tests.Cohere.ApiModel.Embeddings;

public class RequestBodyTests
{
    private const string JSON =
    """
    {
      "texts": [
        "hello",
        "goodbye"
      ],
      "model": "embed-english-v3.0",
      "input_type": "classification",
      "truncate": "END"
    }
    """;

    [Fact]
    public void FromJson_ValidJson_ReturnsRequestBody()
    {
        // Arrange & Act
        var requestBody = RequestBody.FromJson(JSON);

        // Assert
        Assert.NotNull(requestBody);
        Assert.Equal(2, requestBody.Texts.Length);
        Assert.Equal("hello", requestBody.Texts[0]);
        Assert.Equal("goodbye", requestBody.Texts[1]);
        Assert.Equal(AiModel.EmbedEnglishV30, requestBody.Model);
        Assert.Equal(InputType.Classification, requestBody.InputType);
    }

    [Theory]
    [InlineData(JSON)]
    public void ToJson_ValidRequestBody_ReturnsJson(string json)
    {
        // Arrange
        var requestBody = RequestBody.FromJson(json);

        // Act
        string result = requestBody!.ToJson(pretty: true);

        // Assert
        Assert.Equal(json.Replace("\r\n", "\n"), result);
    }
}
