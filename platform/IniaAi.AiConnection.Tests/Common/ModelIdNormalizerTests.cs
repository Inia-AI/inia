using IniaAi.AiConnection.Common;

namespace IniaAi.AiConnection.Tests.Common;

public class ModelIdNormalizerTests
{
    #region Normalize - New 4.6 models (no date suffix, optional :0)

    [Theory]
    [InlineData("us.anthropic.claude-opus-4-6-v1", "claude-opus-4-6")]
    [InlineData("anthropic.claude-opus-4-6-v1", "claude-opus-4-6")]
    [InlineData("eu.anthropic.claude-opus-4-6-v1", "claude-opus-4-6")]
    [InlineData("us.anthropic.claude-sonnet-4-6", "claude-sonnet-4-6")]
    [InlineData("anthropic.claude-sonnet-4-6", "claude-sonnet-4-6")]
    [InlineData("eu.anthropic.claude-sonnet-4-6", "claude-sonnet-4-6")]
    [InlineData("global.anthropic.claude-sonnet-4-6", "claude-sonnet-4-6")]
    public void Normalize_New46Models_ExtractsModelName(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.Normalize(input));
    }

    #endregion

    #region Normalize - Adaptive-generation models (dateless, no -v suffix)

    [Theory]
    [InlineData("us.anthropic.claude-opus-4-8", "claude-opus-4-8")]
    [InlineData("anthropic.claude-opus-4-8", "claude-opus-4-8")]
    [InlineData("eu.anthropic.claude-opus-4-8", "claude-opus-4-8")]
    [InlineData("global.anthropic.claude-opus-4-8", "claude-opus-4-8")]
    [InlineData("us.anthropic.claude-sonnet-5", "claude-sonnet-5")]
    [InlineData("anthropic.claude-sonnet-5", "claude-sonnet-5")]
    [InlineData("global.anthropic.claude-sonnet-5", "claude-sonnet-5")]
    [InlineData("us.anthropic.claude-fable-5", "claude-fable-5")]
    [InlineData("anthropic.claude-fable-5", "claude-fable-5")]
    [InlineData("global.anthropic.claude-fable-5", "claude-fable-5")]
    [InlineData("us.anthropic.claude-opus-5", "claude-opus-5")]
    [InlineData("anthropic.claude-opus-5", "claude-opus-5")]
    [InlineData("eu.anthropic.claude-opus-5", "claude-opus-5")]
    [InlineData("global.anthropic.claude-opus-5", "claude-opus-5")]
    [InlineData("us.anthropic.claude-opus-5-5", "claude-opus-5-5")]
    [InlineData("anthropic.claude-opus-5-5", "claude-opus-5-5")]
    [InlineData("eu.anthropic.claude-opus-5-5", "claude-opus-5-5")]
    [InlineData("global.anthropic.claude-opus-5-5", "claude-opus-5-5")]
    [InlineData("us.anthropic.claude-sonnet-5-5", "claude-sonnet-5-5")]
    [InlineData("anthropic.claude-sonnet-5-5", "claude-sonnet-5-5")]
    [InlineData("eu.anthropic.claude-sonnet-5-5", "claude-sonnet-5-5")]
    [InlineData("global.anthropic.claude-sonnet-5-5", "claude-sonnet-5-5")]
    [InlineData("us.anthropic.claude-haiku-5-5", "claude-haiku-5-5")]
    [InlineData("anthropic.claude-haiku-5-5", "claude-haiku-5-5")]
    [InlineData("eu.anthropic.claude-haiku-5-5", "claude-haiku-5-5")]
    [InlineData("global.anthropic.claude-haiku-5-5", "claude-haiku-5-5")]
    public void Normalize_AdaptiveGenerationModels_ExtractsModelName(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("claude-opus-4-8", "claude-opus-4-8")]
    [InlineData("claude-sonnet-5", "claude-sonnet-5")]
    [InlineData("claude-fable-5", "claude-fable-5")]
    [InlineData("claude-opus-5", "claude-opus-5")]
    [InlineData("claude-opus-5-5", "claude-opus-5-5")]
    [InlineData("claude-sonnet-5-5", "claude-sonnet-5-5")]
    [InlineData("claude-haiku-5-5", "claude-haiku-5-5")]
    public void Normalize_AdaptiveGenerationPublicApiFormat_ReturnsUnchanged(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("us.anthropic.claude-opus-4-8", "aws-bedrock")]
    [InlineData("anthropic.claude-sonnet-5", "aws-bedrock")]
    [InlineData("us.anthropic.claude-fable-5", "aws-bedrock")]
    [InlineData("claude-opus-4-8", "anthropic-api")]
    [InlineData("claude-sonnet-5", "anthropic-api")]
    [InlineData("claude-fable-5", "anthropic-api")]
    [InlineData("us.anthropic.claude-opus-5-5", "aws-bedrock")]
    [InlineData("global.anthropic.claude-opus-5", "aws-bedrock")]
    [InlineData("claude-opus-5-5", "anthropic-api")]
    [InlineData("us.anthropic.claude-sonnet-5-5", "aws-bedrock")]
    [InlineData("claude-sonnet-5-5", "anthropic-api")]
    [InlineData("us.anthropic.claude-haiku-5-5", "aws-bedrock")]
    [InlineData("claude-haiku-5-5", "anthropic-api")]
    public void GetProvider_AdaptiveGenerationModels_ReturnsCorrectProvider(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.GetProvider(input));
    }

    [Theory]
    [InlineData("us.anthropic.claude-opus-4-8", "anthropic")]
    [InlineData("claude-sonnet-5", "anthropic")]
    [InlineData("us.anthropic.claude-fable-5", "anthropic")]
    [InlineData("claude-fable-5", "anthropic")]
    public void GetFamily_AdaptiveGenerationModels_ReturnsAnthropic(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.GetFamily(input));
    }

    #endregion

    #region Normalize - Public API passthrough

    [Theory]
    [InlineData("claude-opus-4-6", "claude-opus-4-6")]
    [InlineData("claude-sonnet-4-6", "claude-sonnet-4-6")]
    [InlineData("claude-opus-4-5-20251101", "claude-opus-4-5-20251101")]
    [InlineData("claude-sonnet-4-5-20250929", "claude-sonnet-4-5-20250929")]
    [InlineData("claude-haiku-4-5-20251001", "claude-haiku-4-5-20251001")]
    public void Normalize_PublicApiFormat_ReturnsUnchanged(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.Normalize(input));
    }

    #endregion

    #region Normalize - Existing models with -v1:0 suffix (regression)

    [Theory]
    [InlineData("us.anthropic.claude-opus-4-5-20251101-v1:0", "claude-opus-4-5-20251101")]
    [InlineData("us.anthropic.claude-sonnet-4-5-20250929-v1:0", "claude-sonnet-4-5-20250929")]
    [InlineData("us.anthropic.claude-haiku-4-5-20251001-v1:0", "claude-haiku-4-5-20251001")]
    [InlineData("us.anthropic.claude-opus-4-1-20250805-v1:0", "claude-opus-4-1-20250805")]
    [InlineData("anthropic.claude-3-5-sonnet-20241022-v2:0", "claude-3-5-sonnet-20241022")]
    [InlineData("eu.anthropic.claude-3-haiku-20240307-v1:0", "claude-3-haiku-20240307")]
    public void Normalize_ExistingBedrockModels_ExtractsModelName(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.Normalize(input));
    }

    #endregion

    #region Normalize - Edge cases

    [Fact]
    public void Normalize_NullOrEmpty_ReturnsUnknown()
    {
        Assert.Equal("unknown", ModelIdNormalizer.Normalize(null));
        Assert.Equal("unknown", ModelIdNormalizer.Normalize(""));
        Assert.Equal("unknown", ModelIdNormalizer.Normalize("   "));
    }

    #endregion

    #region IsBedrockFormat

    [Theory]
    [InlineData("anthropic.claude-opus-4-6-v1", true)]
    [InlineData("us.anthropic.claude-sonnet-4-6", true)]
    [InlineData("us.anthropic.claude-opus-4-5-20251101-v1:0", true)]
    [InlineData("claude-opus-4-6", false)]
    [InlineData("claude-sonnet-4-6", false)]
    public void IsBedrockFormat_DetectsCorrectly(string input, bool expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.IsBedrockFormat(input));
    }

    #endregion

    #region GetProvider

    [Theory]
    [InlineData("us.anthropic.claude-opus-4-6-v1", "aws-bedrock")]
    [InlineData("us.anthropic.claude-sonnet-4-6", "aws-bedrock")]
    [InlineData("claude-opus-4-6", "anthropic-api")]
    [InlineData("claude-sonnet-4-6", "anthropic-api")]
    public void GetProvider_New46Models_ReturnsCorrectProvider(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.GetProvider(input));
    }

    #endregion

    #region GetFamily

    [Theory]
    [InlineData("us.anthropic.claude-opus-4-6-v1", "anthropic")]
    [InlineData("claude-sonnet-4-6", "anthropic")]
    [InlineData("us.anthropic.claude-sonnet-4-6", "anthropic")]
    public void GetFamily_New46Models_ReturnsAnthropic(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.GetFamily(input));
    }

    #endregion

    #region Foundry deployment names — table-driven, keyed on the configured name

    private static readonly ModelDeploymentTable _foundryDeployments = ModelDeploymentTable.Empty.With(
        ModelProviders.AZURE_FOUNDRY,
        ["claude-sonnet-5", "claude-haiku-4-5", "claude-opus-4-8", "claude-sonnet-4-6", "claude-opus-5-5", "tenant-sonnet"]);

    [Theory]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-haiku-4-5")]
    [InlineData("claude-opus-4-8")]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("claude-opus-5-5")]
    [InlineData("tenant-sonnet")]
    public void GetProvider_RegisteredFoundryDeploymentName_ReportsAzureFoundry(string deploymentName)
    {
        Assert.Equal(ModelProviders.AZURE_FOUNDRY, ModelIdNormalizer.GetProvider(deploymentName, _foundryDeployments));
    }

    [Theory]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-haiku-4-5")]
    [InlineData("tenant-sonnet")]
    public void Normalize_RegisteredFoundryDeploymentName_ReturnsTheConfiguredName(string deploymentName)
    {
        Assert.Equal(deploymentName, ModelIdNormalizer.Normalize(deploymentName, _foundryDeployments));
    }

    [Theory]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-haiku-4-5")]
    public void GetFamily_RegisteredFoundryDeploymentName_ReportsAnthropic(string deploymentName)
    {
        Assert.Equal("anthropic", ModelIdNormalizer.GetFamily(deploymentName, _foundryDeployments));
    }

    [Theory]
    [InlineData("us.anthropic.claude-sonnet-5", "aws-bedrock")]
    [InlineData("anthropic.claude-haiku-4-5-20251001-v1:0", "aws-bedrock")]
    [InlineData("global.anthropic.claude-opus-4-8", "aws-bedrock")]
    [InlineData("claude-sonnet-4-5-20250929", "anthropic-api")]
    [InlineData("claude-fable-5", "anthropic-api")]
    [InlineData("embed-v4.0", "cohere-api")]
    public void GetProvider_WithFoundryDeploymentsRegistered_LeavesEveryOtherParseUnchanged(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.GetProvider(input, _foundryDeployments));
    }

    [Theory]
    [InlineData("us.anthropic.claude-sonnet-5", "claude-sonnet-5")]
    [InlineData("anthropic.claude-haiku-4-5-20251001-v1:0", "claude-haiku-4-5-20251001")]
    public void Normalize_WithFoundryDeploymentsRegistered_StillStripsBedrockAffixes(string input, string expected)
    {
        Assert.Equal(expected, ModelIdNormalizer.Normalize(input, _foundryDeployments));
    }

    [Fact]
    public void GetProvider_RegisteredName_MatchesExactly_NeverByCase()
    {
        Assert.Equal("anthropic-api", ModelIdNormalizer.GetProvider("Claude-Sonnet-5", _foundryDeployments));
    }

    [Fact]
    public void GetProvider_WithNoRegisteredDeployments_ADeploymentShapedNameStillReadsAsAnthropicApi()
    {
        Assert.Equal("anthropic-api", ModelIdNormalizer.GetProvider("claude-sonnet-5", ModelDeploymentTable.Empty));
    }

    [Fact]
    public void RegisterDeployments_IsAdditive_AndReachesTheParameterlessOverloads()
    {
        const string first = "registered-test-sonnet";
        const string second = "registered-test-haiku";

        ModelIdNormalizer.RegisterDeployments(ModelProviders.AZURE_FOUNDRY, [first]);
        ModelIdNormalizer.RegisterDeployments(ModelProviders.AZURE_FOUNDRY, [second]);

        Assert.Equal(ModelProviders.AZURE_FOUNDRY, ModelIdNormalizer.GetProvider(first));
        Assert.Equal(ModelProviders.AZURE_FOUNDRY, ModelIdNormalizer.GetProvider(second));
        Assert.Equal(first, ModelIdNormalizer.Normalize(first));
        Assert.True(ModelIdNormalizer.Deployments.TryGetProvider(first, out _));
    }

    #endregion
}
