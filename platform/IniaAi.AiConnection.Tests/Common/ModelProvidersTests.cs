using IniaAi.AiConnection.Common;
using IniaAi.Configuration.Platform;

namespace IniaAi.AiConnection.Tests.Common;

public class ModelProvidersTests
{
    private const string FOUNDRY_SONNET = "claude-sonnet-5";
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5";
    private const string BEDROCK_HAIKU = "anthropic.claude-haiku-4-5-20251001-v1:0";

    [Theory]
    [InlineData(DeploymentMode.Gateway, ModelProviders.AWS_BEDROCK)]
    [InlineData(DeploymentMode.Azure, ModelProviders.AZURE_FOUNDRY)]
    [InlineData(DeploymentMode.Aws, ModelProviders.AWS_BEDROCK)]
    public void ForDeploymentMode_DeclaresTheProviderTheModelCallsGoThrough(DeploymentMode mode, string expected)
    {
        Assert.Equal(expected, ModelProviders.ForDeploymentMode(mode));
    }

    [Fact]
    public void ForDeploymentMode_DirectApi_DeclaresNoProvider()
    {
        Assert.Null(ModelProviders.ForDeploymentMode(DeploymentMode.DirectApi));
    }

    [Theory]
    [InlineData(BEDROCK_SONNET)]
    [InlineData(BEDROCK_HAIKU)]
    public void DescribeModelIdRefusal_AzureFoundry_RefusesABedrockInferenceProfileId(string modelId)
    {
        string? refusal = ModelProviders.DescribeModelIdRefusal(ModelProviders.AZURE_FOUNDRY, modelId);

        Assert.NotNull(refusal);
        Assert.Contains(modelId, refusal);
        Assert.Contains("Bedrock", refusal);
        Assert.Contains("Foundry", refusal);
    }

    [Theory]
    [InlineData(FOUNDRY_SONNET)]
    [InlineData("claude-haiku-4-5")]
    [InlineData("tenant-sonnet")]
    public void DescribeModelIdRefusal_AzureFoundry_AcceptsADeploymentName(string modelId)
    {
        Assert.Null(ModelProviders.DescribeModelIdRefusal(ModelProviders.AZURE_FOUNDRY, modelId));
    }

    [Theory]
    [InlineData(FOUNDRY_SONNET)]
    [InlineData("claude-haiku-4-5")]
    public void DescribeModelIdRefusal_AwsBedrock_RefusesADeploymentName(string modelId)
    {
        string? refusal = ModelProviders.DescribeModelIdRefusal(ModelProviders.AWS_BEDROCK, modelId);

        Assert.NotNull(refusal);
        Assert.Contains(modelId, refusal);
        Assert.Contains("Bedrock", refusal);
    }

    [Theory]
    [InlineData(BEDROCK_SONNET)]
    [InlineData(BEDROCK_HAIKU)]
    public void DescribeModelIdRefusal_AwsBedrock_AcceptsAnInferenceProfileId(string modelId)
    {
        Assert.Null(ModelProviders.DescribeModelIdRefusal(ModelProviders.AWS_BEDROCK, modelId));
    }

    [Theory]
    [InlineData(ModelProviders.ANTHROPIC_API, FOUNDRY_SONNET)]
    [InlineData(ModelProviders.ANTHROPIC_API, BEDROCK_SONNET)]
    [InlineData("llm-gateway", BEDROCK_SONNET)]
    public void DescribeModelIdRefusal_AProviderWithoutAShapeRule_AcceptsAnything(string provider, string modelId)
    {
        Assert.Null(ModelProviders.DescribeModelIdRefusal(provider, modelId));
    }

    [Fact]
    public void DescribeAllowlistRefusal_NamesTheFirstOffender()
    {
        string? refusal = ModelProviders.DescribeAllowlistRefusal(
            ModelProviders.AZURE_FOUNDRY, [FOUNDRY_SONNET, BEDROCK_SONNET, BEDROCK_HAIKU]);

        Assert.NotNull(refusal);
        Assert.Contains(BEDROCK_SONNET, refusal);
        Assert.DoesNotContain(BEDROCK_HAIKU, refusal);
    }

    [Fact]
    public void DescribeAllowlistRefusal_AnAllowlistOfTheRightShape_IsAccepted()
    {
        Assert.Null(ModelProviders.DescribeAllowlistRefusal(ModelProviders.AZURE_FOUNDRY, [FOUNDRY_SONNET, "claude-haiku-4-5"]));
        Assert.Null(ModelProviders.DescribeAllowlistRefusal(ModelProviders.AWS_BEDROCK, [BEDROCK_SONNET, BEDROCK_HAIKU]));
    }

    [Fact]
    public void NamesDeployments_OnlyFoundryIdentifiesModelsByAConfiguredName()
    {
        Assert.True(ModelProviders.NamesDeployments(ModelProviders.AZURE_FOUNDRY));
        Assert.False(ModelProviders.NamesDeployments(ModelProviders.AWS_BEDROCK));
        Assert.False(ModelProviders.NamesDeployments(ModelProviders.ANTHROPIC_API));
    }
}
