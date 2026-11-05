using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Cohere;
using IniaAi.Configuration;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.AiConnection.Tests.Cohere;

public class AzureCohereEmbeddingsConnectorTests
{
    private readonly ITestOutputHelper _output;

    public AzureCohereEmbeddingsConnectorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Constructor_WhenCohereApiUrlIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new PlatformSettings
        {
            AzureFoundry = new AzureFoundrySettings
            {
                CohereApiUrl = null,
                CohereApiKey = "test-key",
                CohereDeploymentName = "cohere-embed-v4"
            }
        };
        IOptions<PlatformSettings> options = Options.Create(settings);
        ILogger<IEmbeddingsConnector> logger = TestsLogger.CreateLogger<IEmbeddingsConnector>(_output);

        // Act & Assert
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new AzureCohereEmbeddingsConnector(logger, options));
        Assert.Contains("Cohere API URL", ex.Message);
    }

    [Fact]
    public void Constructor_WhenCohereDeploymentNameIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new PlatformSettings
        {
            AzureFoundry = new AzureFoundrySettings
            {
                CohereApiUrl = "https://test.services.ai.azure.com/models/embeddings?api-version=2024-05-01-preview",
                CohereApiKey = "test-key",
                CohereDeploymentName = null
            }
        };
        IOptions<PlatformSettings> options = Options.Create(settings);
        ILogger<IEmbeddingsConnector> logger = TestsLogger.CreateLogger<IEmbeddingsConnector>(_output);

        // Act & Assert
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new AzureCohereEmbeddingsConnector(logger, options));
        Assert.Contains("CohereDeploymentName", ex.Message);
    }

    [Fact]
    public void Constructor_WhenApiKeyModeAndCohereApiKeyIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new PlatformSettings
        {
            AzureFoundry = new AzureFoundrySettings
            {
                CohereApiUrl = "https://test.services.ai.azure.com/models/embeddings?api-version=2024-05-01-preview",
                CohereApiKey = null,
                CohereDeploymentName = "cohere-embed-v4",
                UseEntraId = false
            }
        };
        IOptions<PlatformSettings> options = Options.Create(settings);
        ILogger<IEmbeddingsConnector> logger = TestsLogger.CreateLogger<IEmbeddingsConnector>(_output);

        // Act & Assert
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new AzureCohereEmbeddingsConnector(logger, options));
        Assert.Contains("Cohere API key", ex.Message);
    }

    [Fact]
    public void Constructor_WhenEntraIdModeAndNoTokenProvider_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new PlatformSettings
        {
            AzureFoundry = new AzureFoundrySettings
            {
                CohereApiUrl = "https://test.services.ai.azure.com/models/embeddings?api-version=2024-05-01-preview",
                CohereDeploymentName = "cohere-embed-v4",
                UseEntraId = true
            }
        };
        IOptions<PlatformSettings> options = Options.Create(settings);
        ILogger<IEmbeddingsConnector> logger = TestsLogger.CreateLogger<IEmbeddingsConnector>(_output);

        // Act & Assert
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new AzureCohereEmbeddingsConnector(logger, options, azureTokenProvider: null));
        Assert.Contains("token provider", ex.Message);
    }

    [Fact]
    public void Constructor_WhenApiKeyModeAndValidConfig_CreatesSuccessfully()
    {
        // Arrange
        var settings = new PlatformSettings
        {
            AzureFoundry = new AzureFoundrySettings
            {
                CohereApiUrl = "https://test.services.ai.azure.com/models/embeddings?api-version=2024-05-01-preview",
                CohereApiKey = "test-api-key",
                CohereDeploymentName = "cohere-embed-v4",
                UseEntraId = false
            }
        };
        IOptions<PlatformSettings> options = Options.Create(settings);
        ILogger<IEmbeddingsConnector> logger = TestsLogger.CreateLogger<IEmbeddingsConnector>(_output);

        // Act & Assert — should not throw
        var connector = new AzureCohereEmbeddingsConnector(logger, options);
        Assert.Contains("Cohere (Azure)", connector.ModelName);
    }
}
