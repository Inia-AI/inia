using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.Core.Tests.Services.AgentRuntime;

public class AgentModelTiersTests
{
    private const string NEUTRAL_OPUS = "claude-opus-5-5";
    private const string NEUTRAL_SONNET = "claude-sonnet-5-5";
    private const string NEUTRAL_HAIKU = "claude-haiku-5-5";
    private const string BEDROCK_OPUS = "us.anthropic.claude-opus-5-5";
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5-5";
    private const string BEDROCK_HAIKU = "us.anthropic.claude-haiku-5-5";
    private const string ORG = "Org-A";
    private const string OTHER_ORG = "org-b";

    private static AgentRuntimeSettings Runtime(params string[] allowedModels)
    {
        return new AgentRuntimeSettings { Enabled = true, AllowedModels = [.. allowedModels] };
    }

    private static ModelRoutingSettings Routing(string? agentAdvancedModel, string? agentBasicModel)
    {
        return new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                [ORG] = new() { AgentAdvancedModel = agentAdvancedModel, AgentBasicModel = agentBasicModel },
            },
        };
    }

    [Fact]
    public void For_NoConfiguration_UsesTheCodeDefaults_OpusAdvancedAndSonnetBasic()
    {
        AgentModelTiers tiers = AgentModelTiers.For(new AgentRuntimeSettings());

        Assert.Equal(NEUTRAL_OPUS, tiers.AdvancedModel);
        Assert.Equal(NEUTRAL_SONNET, tiers.BasicModel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void For_BlankDeploymentSetting_FallsBackToTheCodeDefault(string? blank)
    {
        var runtime = new AgentRuntimeSettings { AdvancedModel = blank!, BasicModel = blank! };

        AgentModelTiers tiers = AgentModelTiers.For(runtime);

        Assert.Equal(AgentRuntimeSettings.DEFAULT_ADVANCED_MODEL, tiers.AdvancedModel);
        Assert.Equal(AgentRuntimeSettings.DEFAULT_BASIC_MODEL, tiers.BasicModel);
    }

    [Fact]
    public void For_DeploymentSetting_WinsOverTheCodeDefault()
    {
        var runtime = new AgentRuntimeSettings { AdvancedModel = NEUTRAL_SONNET, BasicModel = NEUTRAL_HAIKU };

        AgentModelTiers tiers = AgentModelTiers.For(runtime);

        Assert.Equal(NEUTRAL_SONNET, tiers.AdvancedModel);
        Assert.Equal(NEUTRAL_HAIKU, tiers.BasicModel);
    }

    [Fact]
    public void For_OrganizationOverride_WinsOverTheDeploymentSetting_CaseInsensitively()
    {
        var runtime = new AgentRuntimeSettings { AdvancedModel = NEUTRAL_OPUS, BasicModel = NEUTRAL_SONNET };

        AgentModelTiers tiers = AgentModelTiers.For(runtime, Routing(NEUTRAL_SONNET, NEUTRAL_HAIKU), ORG.ToLowerInvariant());

        Assert.Equal(NEUTRAL_SONNET, tiers.AdvancedModel);
        Assert.Equal(NEUTRAL_HAIKU, tiers.BasicModel);
    }

    [Fact]
    public void For_OrganizationOverridingOneTier_KeepsTheDeploymentsOtherTier()
    {
        AgentModelTiers tiers = AgentModelTiers.For(new AgentRuntimeSettings(), Routing(null, NEUTRAL_HAIKU), ORG);

        Assert.Equal(NEUTRAL_OPUS, tiers.AdvancedModel);
        Assert.Equal(NEUTRAL_HAIKU, tiers.BasicModel);
    }

    [Fact]
    public void For_AnotherOrganization_UsesTheDeploymentTiers()
    {
        AgentModelTiers tiers = AgentModelTiers.For(new AgentRuntimeSettings(), Routing(NEUTRAL_HAIKU, NEUTRAL_HAIKU), OTHER_ORG);

        Assert.Equal(NEUTRAL_OPUS, tiers.AdvancedModel);
        Assert.Equal(NEUTRAL_SONNET, tiers.BasicModel);
    }

    [Fact]
    public void For_AiQueryOverridesOnly_DoNotChangeTheAgentTiers()
    {
        var routing = new ModelRoutingSettings
        {
            TenantModelOverrides = new Dictionary<string, TenantModelOverride>
            {
                [ORG] = new() { AdvancedModel = NEUTRAL_HAIKU, BasicModel = NEUTRAL_HAIKU },
            },
        };

        AgentModelTiers tiers = AgentModelTiers.For(new AgentRuntimeSettings(), routing, ORG);

        Assert.Equal(NEUTRAL_OPUS, tiers.AdvancedModel);
        Assert.Equal(NEUTRAL_SONNET, tiers.BasicModel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfiguredModelOf_SetModel_WinsOverEitherTier(bool shouldUseAdvancedModel)
    {
        var props = new AgentProperties { Model = NEUTRAL_HAIKU, ShouldUseAdvancedModel = shouldUseAdvancedModel };

        Assert.False(AgentModelTiers.UsesTier(props));
        Assert.Equal(NEUTRAL_HAIKU, AgentModelTiers.For(new AgentRuntimeSettings()).ConfiguredModelOf(props));
    }

    [Theory]
    [InlineData(null, true, NEUTRAL_OPUS)]
    [InlineData("", true, NEUTRAL_OPUS)]
    [InlineData("  ", false, NEUTRAL_SONNET)]
    [InlineData(null, false, NEUTRAL_SONNET)]
    public void ConfiguredModelOf_BlankModel_UsesTheTier(string? model, bool shouldUseAdvancedModel, string expected)
    {
        var props = new VerificationProperties { Model = model, ShouldUseAdvancedModel = shouldUseAdvancedModel };

        Assert.True(AgentModelTiers.UsesTier(props));
        Assert.Equal(expected, AgentModelTiers.For(new AgentRuntimeSettings()).ConfiguredModelOf(props));
    }

    [Theory]
    [InlineData(true, BEDROCK_OPUS)]
    [InlineData(false, BEDROCK_SONNET)]
    public void RunModelOf_BlankModel_BedrockAllowlist_RunsTheTiersInferenceProfile(bool shouldUseAdvancedModel, string expected)
    {
        var props = new AgentProperties { ShouldUseAdvancedModel = shouldUseAdvancedModel };

        string runModel = AgentModelTiers.For(Runtime(BEDROCK_SONNET, BEDROCK_OPUS, BEDROCK_HAIKU)).RunModelOf(props);

        Assert.Equal(expected, runModel);
    }

    [Theory]
    [InlineData(true, NEUTRAL_OPUS)]
    [InlineData(false, NEUTRAL_SONNET)]
    public void RunModelOf_BlankModel_FoundryAllowlist_RunsTheTiersDeploymentName(bool shouldUseAdvancedModel, string expected)
    {
        var props = new AgentProperties { ShouldUseAdvancedModel = shouldUseAdvancedModel };

        string runModel = AgentModelTiers.For(Runtime(NEUTRAL_SONNET, NEUTRAL_OPUS)).RunModelOf(props);

        Assert.Equal(expected, runModel);
    }

    [Fact]
    public void RunModelOf_BedrockFormOrganizationOverride_RunsTheFoundryDeploymentOfTheSameModel()
    {
        var props = new AgentProperties { ShouldUseAdvancedModel = false };
        AgentModelTiers tiers = AgentModelTiers.For(Runtime(NEUTRAL_SONNET, NEUTRAL_HAIKU), Routing(null, BEDROCK_HAIKU), ORG);

        Assert.Equal(NEUTRAL_HAIKU, tiers.RunModelOf(props));
    }

    [Fact]
    public void RunModelOf_TierModelOutsideTheAllowlist_RunsTheIdAsGiven_SoTheRuntimeRefusesIt()
    {
        var props = new AgentProperties { ShouldUseAdvancedModel = true };

        Assert.Equal(NEUTRAL_OPUS, AgentModelTiers.For(Runtime(BEDROCK_SONNET)).RunModelOf(props));
    }
}
