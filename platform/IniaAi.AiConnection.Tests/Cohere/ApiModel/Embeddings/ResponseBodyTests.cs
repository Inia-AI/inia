using IniaAi.AiConnection.Cohere.ApiModel.Embeddings;

namespace IniaAi.AiConnection.Tests.Cohere.ApiModel.Embeddings;

public class ResponseBodyTests
{
    private const string JSON =
    """
    {
      "response_type": "embeddings_floats",
      "id": "string",
      "embeddings": [
        [
          0.0038915484,
          0.010964915,
          -0.035594109,
          0.011034692
        ]
      ],
      "texts": [
        "string"
      ],
      "meta": {
        "api_version": {
          "version": "string",
          "is_deprecated": true,
          "is_experimental": true
        },
        "billed_units": {
          "input_tokens": 1,
          "output_tokens": 2,
          "search_units": 3,
          "classifications": 4
        },
        "tokens": {
          "input_tokens": 5,
          "output_tokens": 6
        },
        "warnings": [
          "string"
        ]
      }
    }
    """;

    [Fact]
    public void FromJson_ValidJson_ReturnsResponseBody()
    {
        // Arrange & Act
        var responseBody = ResponseBody.FromJson(JSON);

        // Assert
        Assert.NotNull(responseBody);
        Assert.Equal("embeddings_floats", responseBody.ResponseType);
        Assert.Equal("string", responseBody.Id);
        _ = Assert.Single(responseBody.Embeddings);
        Assert.Equal(4, responseBody.Embeddings[0].Length);
        Assert.Equal(0.0038915484, responseBody.Embeddings[0][0]);
        Assert.Equal(0.010964915, responseBody.Embeddings[0][1]);
        Assert.Equal(-0.035594109, responseBody.Embeddings[0][2]);
        Assert.Equal(0.011034692, responseBody.Embeddings[0][3]);
        _ = Assert.Single(responseBody.Texts);
        Assert.Equal("string", responseBody.Texts[0]);
        Assert.Equal("string", responseBody.Meta.ApiVersion.Version);
        Assert.True(responseBody.Meta.ApiVersion.IsDeprecated);
        Assert.True(responseBody.Meta.ApiVersion.IsExperimental);
        Assert.Equal(1, responseBody.Meta.BilledUnits.InputTokens);
        Assert.Equal(2, responseBody.Meta.BilledUnits.OutputTokens);
        Assert.Equal(3, responseBody.Meta.BilledUnits.SearchUnits);
        Assert.Equal(4, responseBody.Meta.BilledUnits.Classifications);
        Assert.Equal(5, responseBody.Meta.Tokens.InputTokens);
        Assert.Equal(6, responseBody.Meta.Tokens.OutputTokens);
        _ = Assert.Single(responseBody.Meta.Warnings);
        Assert.Equal("string", responseBody.Meta.Warnings[0]);
    }

    [Theory]
    [InlineData(JSON)]
    public void ToJson_ValidResponseBody_ReturnsJson(string json)
    {
        // Arrange
        var responseBody = ResponseBody.FromJson(json);

        // Act
        string result = responseBody!.ToJson(pretty: true);

        // Assert
        Assert.Equal(json.Replace("\r\n", "\n"), result);
    }
}
