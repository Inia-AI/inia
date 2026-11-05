using IniaAi.AiConnection.Common;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Tests;

/// <summary>
/// The platform's allowlist mirrors the runtime's byte for byte, and the deployment mode says
/// what shape those ids have: Bedrock inference-profile ids on AWS and behind a model gateway,
/// Foundry deployment names on Azure. A list of the wrong shape refuses startup — the same fail-closed posture as a missing
/// auth token — and on Azure the names register as Foundry deployments so usage attributed to
/// them is priced and reported as <c>azure-foundry</c>.
/// </summary>
public class AgentRuntimeClientRegistrationTests
{
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5";
    private const string BEDROCK_HAIKU = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
    private const string FOUNDRY_SONNET = "registration-test-sonnet";
    private const string FOUNDRY_HAIKU = "registration-test-haiku";

    private static AgentRuntimeSettings Runtime(bool enabled, params string[] allowedModels)
    {
        return new AgentRuntimeSettings
        {
            Enabled = enabled,
            AuthToken = "configured-elsewhere",
            AllowedModels = [.. allowedModels],
        };
    }

    [Fact]
    public void AzureMode_FoundryNames_RegisterAsFoundryDeployments()
    {
        _ = new ServiceCollection().AddAgentRuntimeClient(
            Runtime(enabled: true, FOUNDRY_SONNET, FOUNDRY_HAIKU), ModelProviders.AZURE_FOUNDRY);

        Assert.Equal(ModelProviders.AZURE_FOUNDRY, ModelIdNormalizer.GetProvider(FOUNDRY_SONNET));
        Assert.Equal(ModelProviders.AZURE_FOUNDRY, ModelIdNormalizer.GetProvider(FOUNDRY_HAIKU));
    }

    [Fact]
    public void AzureMode_ABedrockIdInTheAllowlist_RefusesStartupNamingTheKeyAndTheId()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddAgentRuntimeClient(
                Runtime(enabled: true, FOUNDRY_SONNET, BEDROCK_SONNET), ModelProviders.AZURE_FOUNDRY));

        Assert.Contains($"{AgentRuntimeSettings.SectionName}:{nameof(AgentRuntimeSettings.AllowedModels)}", exception.Message);
        Assert.Contains(BEDROCK_SONNET, exception.Message);
        Assert.Contains(ModelProviders.AZURE_FOUNDRY, exception.Message);
    }

    [Fact]
    public void AwsMode_ADeploymentNameInTheAllowlist_RefusesStartupNamingTheKeyAndTheId()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddAgentRuntimeClient(
                Runtime(enabled: true, BEDROCK_SONNET, "claude-sonnet-5"), ModelProviders.AWS_BEDROCK));

        Assert.Contains($"{AgentRuntimeSettings.SectionName}:{nameof(AgentRuntimeSettings.AllowedModels)}", exception.Message);
        Assert.Contains("claude-sonnet-5", exception.Message);
        Assert.Contains(ModelProviders.AWS_BEDROCK, exception.Message);
    }

    [Fact]
    public void AwsMode_BedrockIds_StartAndParseAsBedrockWithoutRegistration()
    {
        _ = new ServiceCollection().AddAgentRuntimeClient(
            Runtime(enabled: true, BEDROCK_SONNET, BEDROCK_HAIKU), ModelProviders.AWS_BEDROCK);

        Assert.Equal(ModelProviders.AWS_BEDROCK, ModelIdNormalizer.GetProvider(BEDROCK_SONNET));
        Assert.Equal(ModelProviders.AWS_BEDROCK, ModelIdNormalizer.GetProvider(BEDROCK_HAIKU));
    }

    [Fact]
    public void GatewayMode_ANeutralIdInTheAllowlist_RefusesStartupAsABedrockDeployment()
    {
        var platformSettings = new PlatformSettings
        {
            UsesModelGateway = true,
            AgentRuntime = Runtime(enabled: true, "us.anthropic.claude-sonnet-4-6", "claude-opus-5-5"),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddAgentRuntimeClient(platformSettings));

        Assert.Contains("claude-opus-5-5", exception.Message);
        Assert.Contains(ModelProviders.AWS_BEDROCK, exception.Message);
    }

    [Fact]
    public void GatewayMode_TheGatewaysBedrockIds_Start()
    {
        var platformSettings = new PlatformSettings
        {
            UsesModelGateway = true,
            AgentRuntime = Runtime(enabled: true, "us.anthropic.claude-sonnet-4-6", "us.anthropic.claude-sonnet-5-5",
                "us.anthropic.claude-opus-4-8", "us.anthropic.claude-opus-5", "us.anthropic.claude-opus-5-5"),
        };

        _ = new ServiceCollection().AddAgentRuntimeClient(platformSettings);
    }

    [Fact]
    public void NoDeclaredProvider_AcceptsAnyShape_TheRuntimeAllowlistIsTheOnlyRule()
    {
        _ = new ServiceCollection().AddAgentRuntimeClient(
            Runtime(enabled: true, BEDROCK_SONNET, "claude-sonnet-5"), modelProvider: null);
    }

    [Fact]
    public void DisabledRuntime_SkipsTheShapeCheckAndRegistersNothing()
    {
        const string neverRegistered = "disabled-runtime-test-name";

        _ = new ServiceCollection().AddAgentRuntimeClient(
            Runtime(enabled: false, BEDROCK_SONNET, neverRegistered), ModelProviders.AZURE_FOUNDRY);

        Assert.NotEqual(ModelProviders.AZURE_FOUNDRY, ModelIdNormalizer.GetProvider(neverRegistered));
    }
}
