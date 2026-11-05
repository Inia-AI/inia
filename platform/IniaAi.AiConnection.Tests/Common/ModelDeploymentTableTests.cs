using IniaAi.AiConnection.Common;

namespace IniaAi.AiConnection.Tests.Common;

public class ModelDeploymentTableTests
{
    private const string SONNET = "claude-sonnet-5";
    private const string HAIKU = "claude-haiku-4-5";
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5";

    [Fact]
    public void Empty_KnowsNoName()
    {
        Assert.Equal(0, ModelDeploymentTable.Empty.Count);
        Assert.False(ModelDeploymentTable.Empty.TryGetProvider(SONNET, out _));
    }

    [Fact]
    public void With_MapsEveryNameToTheProvider()
    {
        ModelDeploymentTable table = ModelDeploymentTable.Empty.With(ModelProviders.AZURE_FOUNDRY, [SONNET, HAIKU]);

        Assert.Equal(2, table.Count);
        Assert.True(table.TryGetProvider(SONNET, out string? provider));
        Assert.Equal(ModelProviders.AZURE_FOUNDRY, provider);
        Assert.True(table.TryGetProvider(HAIKU, out _));
    }

    [Fact]
    public void With_LeavesTheOriginalTableUntouched()
    {
        ModelDeploymentTable original = ModelDeploymentTable.Empty;

        _ = original.With(ModelProviders.AZURE_FOUNDRY, [SONNET]);

        Assert.Equal(0, original.Count);
    }

    [Fact]
    public void TryGetProvider_MatchesTheConfiguredNameExactly()
    {
        ModelDeploymentTable table = ModelDeploymentTable.Empty.With(ModelProviders.AZURE_FOUNDRY, [SONNET]);

        Assert.False(table.TryGetProvider("Claude-Sonnet-5", out _));
        Assert.False(table.TryGetProvider(" claude-sonnet-5", out _));
        Assert.False(table.TryGetProvider(null, out _));
        Assert.False(table.TryGetProvider(string.Empty, out _));
    }

    [Fact]
    public void With_TheSameNameForTheSameProviderTwice_IsIdempotent()
    {
        ModelDeploymentTable table = ModelDeploymentTable.Empty
            .With(ModelProviders.AZURE_FOUNDRY, [SONNET])
            .With(ModelProviders.AZURE_FOUNDRY, [SONNET, HAIKU]);

        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void With_ANameAlreadyOwnedByAnotherProvider_Refuses()
    {
        ModelDeploymentTable table = ModelDeploymentTable.Empty.With(ModelProviders.AZURE_FOUNDRY, [SONNET]);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => table.With("llm-gateway", [SONNET]));
        Assert.Contains(SONNET, exception.Message);
        Assert.Contains(ModelProviders.AZURE_FOUNDRY, exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void With_ABlankName_Refuses(string name)
    {
        _ = Assert.Throws<ArgumentException>(
            () => ModelDeploymentTable.Empty.With(ModelProviders.AZURE_FOUNDRY, [name]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void With_ABlankProvider_Refuses(string provider)
    {
        _ = Assert.Throws<ArgumentException>(() => ModelDeploymentTable.Empty.With(provider, [SONNET]));
    }

    [Fact]
    public void With_ABedrockInferenceProfileIdAsAFoundryDeployment_RefusesNamingTheId()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ModelDeploymentTable.Empty.With(ModelProviders.AZURE_FOUNDRY, [SONNET, BEDROCK_SONNET]));

        Assert.Contains(BEDROCK_SONNET, exception.Message);
        Assert.Contains("Bedrock", exception.Message);
    }
}
