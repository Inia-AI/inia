using IniaAi.AiConnection.Azure.ApiModel;

namespace IniaAi.AiConnection.Tests.Azure;

public class AzureInferenceEmbeddingsModelTests
{
    [Fact]
    public void EmbeddingsRequest_SerializesCorrectly()
    {
        // Arrange
        var request = new EmbeddingsRequest
        {
            Input = ["Hello world", "Goodbye"],
            Model = "cohere-embed-v4",
            Dimensions = 1024
        };

        // Act
        string json = request.ToJson(pretty: false);

        // Assert
        Assert.Contains("\"input\":[\"Hello world\",\"Goodbye\"]", json);
        Assert.Contains("\"model\":\"cohere-embed-v4\"", json);
        Assert.Contains("\"dimensions\":1024", json);
    }

    [Fact]
    public void EmbeddingsRequest_OmitsDimensions_WhenNull()
    {
        // Arrange
        var request = new EmbeddingsRequest
        {
            Input = ["test"],
            Model = "cohere-embed-v4",
            Dimensions = null
        };

        // Act
        string json = request.ToJson(pretty: false);

        // Assert
        Assert.DoesNotContain("dimensions", json);
    }

    [Fact]
    public void EmbeddingsResponse_DeserializesRealAzureResponse()
    {
        // Arrange — real Azure response format
        const string json = """
        {
            "data": [
                {
                    "index": 0,
                    "embedding": [0.123, -0.456, 0.789]
                },
                {
                    "index": 1,
                    "embedding": [0.111, -0.222, 0.333]
                }
            ],
            "model": "cohere-embed-v4",
            "usage": {
                "prompt_tokens": 5,
                "total_tokens": 5
            }
        }
        """;

        // Act
        var response = EmbeddingsResponse.FromJson(json);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(2, response.Data.Length);
        Assert.Equal("cohere-embed-v4", response.Model);
        Assert.Equal(5, response.Usage.PromptTokens);
        Assert.Equal(5, response.Usage.TotalTokens);

        // First embedding
        Assert.Equal(0, response.Data[0].Index);
        Assert.Equal([0.123, -0.456, 0.789], response.Data[0].Embedding);

        // Second embedding
        Assert.Equal(1, response.Data[1].Index);
        Assert.Equal([0.111, -0.222, 0.333], response.Data[1].Embedding);
    }

    [Fact]
    public void EmbeddingsResponse_HandlesEmptyDataArray()
    {
        // Arrange
        const string json = """
        {
            "data": [],
            "model": "cohere-embed-v4",
            "usage": {
                "prompt_tokens": 0,
                "total_tokens": 0
            }
        }
        """;

        // Act
        var response = EmbeddingsResponse.FromJson(json);

        // Assert
        Assert.NotNull(response);
        Assert.Empty(response.Data);
        Assert.Equal(0, response.Usage.TotalTokens);
    }

    [Fact]
    public void EmbeddingsResponse_ExtractsEmbeddingsOrderedByIndex()
    {
        // Arrange — deliberately out-of-order indices
        const string json = """
        {
            "data": [
                {
                    "index": 1,
                    "embedding": [0.4, 0.5, 0.6]
                },
                {
                    "index": 0,
                    "embedding": [0.1, 0.2, 0.3]
                }
            ],
            "model": "cohere-embed-v4",
            "usage": {
                "prompt_tokens": 3,
                "total_tokens": 3
            }
        }
        """;

        // Act
        var response = EmbeddingsResponse.FromJson(json);
        var orderedEmbeddings = response!.Data.OrderBy(d => d.Index).Select(d => d.Embedding).ToList();

        // Assert — index 0 should come first after ordering
        Assert.Equal([0.1, 0.2, 0.3], orderedEmbeddings[0]);
        Assert.Equal([0.4, 0.5, 0.6], orderedEmbeddings[1]);
    }
}
