using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;

namespace IniaAi.Core.Tests.Services.AgentRuntime;

public class AgentRuntimeModelsTests
{
    private const string NEUTRAL_SONNET = "claude-sonnet-5-5";
    private const string NEUTRAL_HAIKU = "claude-haiku-5-5";
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5-5";
    private const string EU_SONNET = "eu.anthropic.claude-sonnet-5-5";
    private const string GLOBAL_SONNET = "global.anthropic.claude-sonnet-5-5";
    private const string BEDROCK_HAIKU = "us.anthropic.claude-haiku-5-5";
    private const string GLOBAL_HAIKU = "global.anthropic.claude-haiku-5-5";
    private const string BEDROCK_OPUS = "us.anthropic.claude-opus-5-5";
    private const string BEDROCK_OLD_HAIKU = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
    private const string NEUTRAL_OLD_HAIKU = "claude-haiku-4-5-20251001";
    private const string ROGUE_MODEL = "rogue-model";

    private static AgentRuntimeSettings Runtime(params string[] allowedModels)
    {
        return new AgentRuntimeSettings { Enabled = true, AllowedModels = [.. allowedModels] };
    }

    [Theory]
    [InlineData(BEDROCK_SONNET, BEDROCK_SONNET)]
    [InlineData(NEUTRAL_SONNET, BEDROCK_SONNET)]
    [InlineData(EU_SONNET, BEDROCK_SONNET)]
    [InlineData(GLOBAL_SONNET, BEDROCK_SONNET)]
    [InlineData(NEUTRAL_OLD_HAIKU, BEDROCK_OLD_HAIKU)]
    public void RuntimeIdFor_BedrockAllowlist_ResolvesAnyFormToTheListedProfile(string modelId, string expected)
    {
        Assert.Equal(expected, AgentRuntimeModels.RuntimeIdFor(Runtime(BEDROCK_SONNET, BEDROCK_OLD_HAIKU), modelId));
    }

    [Theory]
    [InlineData(NEUTRAL_SONNET)]
    [InlineData(BEDROCK_SONNET)]
    [InlineData("anthropic.claude-sonnet-5-5")]
    public void RuntimeIdFor_FoundryAllowlist_ResolvesAnyFormToTheDeploymentName(string modelId)
    {
        Assert.Equal(NEUTRAL_SONNET, AgentRuntimeModels.RuntimeIdFor(Runtime(NEUTRAL_SONNET, NEUTRAL_HAIKU), modelId));
    }

    [Theory]
    [InlineData("claude-sonnet-4-6", "us.anthropic.claude-sonnet-4-6")]
    [InlineData("claude-opus-5-5", BEDROCK_OPUS)]
    [InlineData("global.anthropic.claude-opus-4-8", "us.anthropic.claude-opus-4-8")]
    public void RunIdFor_GatewayAllowlist_RunsTheUsProfileTheGatewayServes(string modelId, string expected)
    {
        AgentRuntimeSettings gateway = Runtime(
            "us.anthropic.claude-sonnet-4-6",
            BEDROCK_SONNET,
            "us.anthropic.claude-opus-4-8",
            "us.anthropic.claude-opus-5",
            BEDROCK_OPUS);

        Assert.Equal(expected, AgentRuntimeModels.RunIdFor(gateway, modelId));
    }

    [Theory]
    [InlineData(BEDROCK_OPUS)]
    [InlineData("claude-opus-5-5")]
    [InlineData(ROGUE_MODEL)]
    [InlineData("")]
    [InlineData(null)]
    public void RuntimeIdFor_ModelWithNoListedForm_IsNull(string? modelId)
    {
        Assert.Null(AgentRuntimeModels.RuntimeIdFor(Runtime(BEDROCK_SONNET, NEUTRAL_HAIKU), modelId));
    }

    [Fact]
    public void RuntimeIdFor_ExactListedIdWins_OverAnEarlierFormOfTheSameModel()
    {
        AgentRuntimeSettings runtime = Runtime(GLOBAL_SONNET, BEDROCK_SONNET);

        Assert.Equal(BEDROCK_SONNET, AgentRuntimeModels.RuntimeIdFor(runtime, BEDROCK_SONNET));
        Assert.Equal(GLOBAL_SONNET, AgentRuntimeModels.RuntimeIdFor(runtime, NEUTRAL_SONNET));
    }

    [Theory]
    [InlineData(NEUTRAL_SONNET, BEDROCK_SONNET)]
    [InlineData(BEDROCK_OPUS, BEDROCK_OPUS)]
    [InlineData(null, null)]
    public void RunIdFor_ListedFormElseAsGiven(string? modelId, string? expected)
    {
        Assert.Equal(expected, AgentRuntimeModels.RunIdFor(Runtime(BEDROCK_SONNET), modelId));
    }

    [Theory]
    [InlineData(BEDROCK_SONNET, NEUTRAL_SONNET)]
    [InlineData(NEUTRAL_SONNET, NEUTRAL_SONNET)]
    [InlineData(BEDROCK_OPUS, BEDROCK_OPUS)]
    [InlineData(ROGUE_MODEL, ROGUE_MODEL)]
    [InlineData(null, null)]
    public void StoredIdFor_NeutralWhenAllowed_ElseUnchanged(string? modelId, string? expected)
    {
        Assert.Equal(expected, AgentRuntimeModels.StoredIdFor(Runtime(BEDROCK_SONNET), modelId));
    }

    [Fact]
    public void NeutralAllowedModels_OneEntryPerModel_InAllowlistOrder()
    {
        AgentRuntimeSettings runtime = Runtime(BEDROCK_HAIKU, BEDROCK_SONNET, GLOBAL_HAIKU, BEDROCK_OLD_HAIKU);

        Assert.Equal(
            [NEUTRAL_HAIKU, NEUTRAL_SONNET, NEUTRAL_OLD_HAIKU],
            AgentRuntimeModels.NeutralAllowedModels(runtime));
    }
}
