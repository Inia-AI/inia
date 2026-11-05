using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Cohere;
using IniaAi.Common.Embeddings;
using IniaAi.Configuration;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Testing;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.Options;

namespace IniaAi.AiConnection.Tests.Cohere;

public class CohereEmbeddingsConnectorTests : IClassFixture<TestsFixture<CohereEmbeddingsConnectorTests>>
{
    private const string EMBED_PATH = "/v2/embed";

    private readonly ITestOutputHelper _testOutputHelper;
    private readonly TestsFixture<CohereEmbeddingsConnectorTests> _fixture;

    public CohereEmbeddingsConnectorTests(TestsFixture<CohereEmbeddingsConnectorTests> fixture, ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _fixture = fixture;
    }

    [PaidApiFact("Calls the Cohere embeddings API", alsoInCi: true)]
    public async Task GetResponseAsync_WhenCalled_ReturnsResponseAsync()
    {
        // Arrange
        IEmbeddingsConnector connector = EmbeddingsConnectorFactory.Create(
            _fixture.PlatformSettings,
            TestsLogger.CreateLogger<CohereEmbeddingsConnector>(_testOutputHelper));
        List<string> messages =
        [
            "Cat",      // [0] - animal
            "Germany",  // [1] - country
            "Kitten",   // [2] - baby cat → should match Cat
            "Berlin"    // [3] - German city → should match Germany
        ];

        // Act
        List<double[]> response = await connector.GetResponseAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(4, response.Count);
        Assert.Equal(1024, response[0].Length);
        Assert.Equal(1024, response[1].Length);
        Assert.Equal(1024, response[2].Length);
        Assert.Equal(1024, response[3].Length);
        var vectors = response
            .Select((vector, index) => (index, vector))
            .ToDictionary(x => x.index, x => x.vector);

        // Rank by similarity to "Kitten" (index 2) - should match "Cat" (index 0)
        List<(double Score, int Index)> scores1 = EmbeddingsUtils.RankVectors(vectors, response[2]);
        Assert.Equal(4, scores1.Count);
        Assert.Equal(2, scores1[0].Index); // Self
        Assert.Equal(0, scores1[1].Index); // Cat

        // Rank by similarity to "Berlin" (index 3) - should match "Germany" (index 1)
        List<(double Score, int Index)> scores2 = EmbeddingsUtils.RankVectors(vectors, response[3]);
        Assert.Equal(4, scores2.Count);
        Assert.Equal(3, scores2[0].Index); // Self
        Assert.Equal(1, scores2[1].Index); // Germany
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithoutAKey_TheConnectorBuilds_AndItsFirstCallFailsBeforeSendingAsync(string? apiKey)
    {
        using var cohere = new LocalTestHttpServer();
        var settings = new PlatformSettings
        {
            Cohere = new CohereSettings { ApiKey = apiKey, EmbeddingsApiUrl = $"{cohere.BaseUrl}{EMBED_PATH}" }
        };
        var connector = new CohereEmbeddingsConnector(
            TestsLogger.CreateLogger<IEmbeddingsConnector>(_testOutputHelper),
            Options.Create(settings));

        ApiKeyNotSetException error = await Assert.ThrowsAsync<ApiKeyNotSetException>(
            () => connector.GetResponseAsync(["Cat"], TestContext.Current.CancellationToken));

        Assert.Equal("Cohere API key not set.", error.Message);
        Assert.Equal(0, cohere.PathHits(EMBED_PATH));
    }

    [Theory]
    [InlineData(null, "PlatformSettings:Cohere:ApiKey")]
    [InlineData("", "PlatformSettings:Cohere:ApiKey")]
    [InlineData("a-key", null)]
    public void MissingSetting_NamesTheKey_OnlyWhenItIsNotSet(string? apiKey, string? expected)
    {
        var settings = new PlatformSettings { Cohere = new CohereSettings { ApiKey = apiKey } };
        var connector = new CohereEmbeddingsConnector(
            TestsLogger.CreateLogger<IEmbeddingsConnector>(_testOutputHelper),
            Options.Create(settings));

        Assert.Equal(expected, connector.MissingSetting);
    }
}
