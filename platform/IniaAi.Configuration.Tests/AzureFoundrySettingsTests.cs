using IniaAi.Configuration.LLMProviders;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Tests;

public class AzureFoundrySettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new AzureFoundrySettings();

        // Assert
        Assert.Null(settings.AnthropicApiUrl);
        Assert.Null(settings.AnthropicApiKey);
        Assert.False(settings.UseEntraId);
        Assert.Null(settings.CohereApiUrl);
        Assert.Null(settings.CohereApiKey);
        Assert.Null(settings.CohereDeploymentName);
    }

    [Fact]
    public void PlatformSettings_AzureFoundry_DefaultIsNotNull()
    {
        // Arrange & Act
        var platformSettings = new PlatformSettings();

        // Assert
        Assert.NotNull(platformSettings.AzureFoundry);
    }

    [Fact]
    public void ConfigBinding_FromInMemoryJson_BindsCorrectly()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["PlatformSettings:AzureFoundry:AnthropicApiUrl"] = "https://test.services.ai.azure.com/anthropic/v1/messages",
            ["PlatformSettings:AzureFoundry:AnthropicApiKey"] = "test-key-123",
            ["PlatformSettings:AzureFoundry:UseEntraId"] = "true",
            ["PlatformSettings:AzureFoundry:CohereApiUrl"] = "https://test.services.ai.azure.com/models/embeddings?api-version=2024-05-01-preview",
            ["PlatformSettings:AzureFoundry:CohereApiKey"] = "cohere-key-456",
            ["PlatformSettings:AzureFoundry:CohereDeploymentName"] = "cohere-embed-v4",
        };

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var settings = new PlatformSettings();
        configuration.GetSection("PlatformSettings").Bind(settings);

        // Assert
        Assert.Equal("https://test.services.ai.azure.com/anthropic/v1/messages", settings.AzureFoundry.AnthropicApiUrl);
        Assert.Equal("test-key-123", settings.AzureFoundry.AnthropicApiKey);
        Assert.True(settings.AzureFoundry.UseEntraId);
        Assert.Equal("https://test.services.ai.azure.com/models/embeddings?api-version=2024-05-01-preview", settings.AzureFoundry.CohereApiUrl);
        Assert.Equal("cohere-key-456", settings.AzureFoundry.CohereApiKey);
        Assert.Equal("cohere-embed-v4", settings.AzureFoundry.CohereDeploymentName);
    }
}
