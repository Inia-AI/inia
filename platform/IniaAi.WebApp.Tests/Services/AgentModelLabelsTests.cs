using IniaAi.AiConnection.Common;
using IniaAi.WebApp.Services;

namespace IniaAi.WebApp.Tests.Services;

/// <summary>
/// The model dropdown labels a Bedrock inference-profile id by its catalogue description and a
/// Foundry deployment by the name the operator configured — the name is the deployment's
/// identity, and the catalogue's description for the same string names no deployment.
/// </summary>
public class AgentModelLabelsTests
{
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5";
    private const string BEDROCK_HAIKU = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
    private const string FOUNDRY_SONNET = "claude-sonnet-5";
    private const string FOUNDRY_CUSTOM = "tenant-sonnet";

    private static readonly ModelDeploymentTable _foundry = ModelDeploymentTable.Empty
        .With(ModelProviders.AZURE_FOUNDRY, [FOUNDRY_SONNET, FOUNDRY_CUSTOM]);

    [Fact]
    public void Describe_BedrockInferenceProfileId_UsesTheCatalogueDescription()
    {
        Assert.Equal("AWS Bedrock US Sonnet 5", AgentModelLabels.Describe(BEDROCK_SONNET, _foundry));
    }

    [Theory]
    [InlineData("us.anthropic.claude-sonnet-5-5", "AWS Bedrock US Sonnet 5.5")]
    [InlineData("us.anthropic.claude-opus-5-5", "AWS Bedrock US Opus 5.5")]
    [InlineData("us.anthropic.claude-haiku-5-5", "AWS Bedrock US Haiku 5.5")]
    public void Describe_BedrockFiveFiveModels_UseTheCatalogueDescriptionAndSortByTier(string modelId, string expected)
    {
        Assert.Equal(expected, AgentModelLabels.Describe(modelId, _foundry));
        Assert.NotEqual(int.MaxValue, AgentModelLabels.VisualOrder(modelId, _foundry));
    }

    [Theory]
    [InlineData(FOUNDRY_SONNET, "claude-sonnet-5 (Azure AI Foundry)")]
    [InlineData(FOUNDRY_CUSTOM, "tenant-sonnet (Azure AI Foundry)")]
    public void Describe_FoundryDeploymentName_ShowsTheConfiguredNameAndTheProvider(string modelId, string expected)
    {
        Assert.Equal(expected, AgentModelLabels.Describe(modelId, _foundry));
    }

    [Fact]
    public void Describe_AnUnregisteredPlainId_FallsBackToTheCatalogueThenTheIdItself()
    {
        Assert.Equal("Claude Sonnet 5", AgentModelLabels.Describe(FOUNDRY_SONNET, ModelDeploymentTable.Empty));
        Assert.Equal("some-unknown-id", AgentModelLabels.Describe("some-unknown-id", ModelDeploymentTable.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Describe_Blank_IsEmpty(string? modelId)
    {
        Assert.Equal(string.Empty, AgentModelLabels.Describe(modelId, _foundry));
    }

    [Fact]
    public void VisualOrder_CatalogueModelsSortByTier_AndFoundryNamesKeepTheConfiguredOrder()
    {
        Assert.True(AgentModelLabels.VisualOrder(BEDROCK_HAIKU) < AgentModelLabels.VisualOrder(BEDROCK_SONNET));
        Assert.Equal(int.MaxValue, AgentModelLabels.VisualOrder(FOUNDRY_SONNET, _foundry));
        Assert.Equal(int.MaxValue, AgentModelLabels.VisualOrder(FOUNDRY_CUSTOM, _foundry));
    }
}
