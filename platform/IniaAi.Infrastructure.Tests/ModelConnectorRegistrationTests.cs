using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Azure;
using IniaAi.AiConnection.Cohere;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Tests;

/// <summary>
/// Both hosts register the platform's own chat and embeddings connectors through one method, so the
/// deployment mode picks them in one place.
/// </summary>
public class ModelConnectorRegistrationTests
{
    [Theory]
    [InlineData(DeploymentMode.Azure, typeof(AnthropicChatConnector), typeof(AzureCohereEmbeddingsConnector))]
    [InlineData(DeploymentMode.Aws, typeof(AwsBedrockChatProvider), typeof(CohereAwsEmbeddingsConnector))]
    [InlineData(DeploymentMode.DirectApi, typeof(AnthropicChatConnector), typeof(CohereEmbeddingsConnector))]
    public void EachMode_RegistersItsChatAndEmbeddingsConnectors(DeploymentMode mode, Type chat, Type embeddings)
    {
        ServiceCollection services = new();

        _ = services.AddModelConnectors(new PlatformSettings(), mode);

        Assert.Equal(chat, ImplementationOf<IChatConnector>(services));
        Assert.Equal(embeddings, ImplementationOf<IEmbeddingsConnector>(services));
    }

    [Fact]
    public void GatewayMode_WithoutAGateway_RefusesAndNamesTheSetting()
    {
        ServiceCollection services = new();

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => services.AddModelConnectors(new PlatformSettings { UsesModelGateway = true }));

        Assert.Equal(IModelGateway.NOT_IN_BUILD, refusal.Message);
        Assert.Contains("PlatformSettings:UsesModelGateway", refusal.Message);
    }

    [Theory]
    [InlineData(true, typeof(DefaultAzureTokenProvider))]
    [InlineData(false, typeof(NullAzureTokenProvider))]
    public void AzureMode_PicksTheTokenProviderFromUseEntraId(bool useEntraId, Type expected)
    {
        var settings = new PlatformSettings();
        settings.AzureFoundry.UseEntraId = useEntraId;
        ServiceCollection services = new();

        _ = services.AddModelConnectors(settings, DeploymentMode.Azure);

        Assert.Equal(expected, ImplementationOf<IAzureTokenProvider>(services));
    }

    private static Type? ImplementationOf<TService>(IServiceCollection services)
    {
        return Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(TService)).ImplementationType;
    }
}
