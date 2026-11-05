using IniaAi.Application.Common.Workflows;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class AgentBlockModelNormalizerTests
{
    private const string NEUTRAL_SONNET = "claude-sonnet-5-5";
    private const string BEDROCK_SONNET = "us.anthropic.claude-sonnet-5-5";
    private const string EU_SONNET = "eu.anthropic.claude-sonnet-5-5";
    private const string BEDROCK_OPUS = "us.anthropic.claude-opus-5-5";

    private static readonly AgentRuntimeSettings _bedrockRuntime = new()
    {
        Enabled = true,
        AllowedModels = [BEDROCK_SONNET],
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Normalize_LeavesABlankOverrideBlank_SoTheTierStillDecides(string? model)
    {
        var workflow = new Workflow("Models") { Title = "Models" };
        var section = new Section("Body", workflow);
        var agent = new Block(BlockTypes.Agent, section, "agent");
        var props = (AgentProperties)agent.Properties;
        props.Model = model;
        props.ShouldUseAdvancedModel = false;

        int changed = AgentBlockModelNormalizer.Normalize(workflow, _bedrockRuntime);

        Assert.Equal(0, changed);
        Assert.Equal(model, props.Model);
        Assert.False(props.ShouldUseAdvancedModel);
    }

    [Fact]
    public void Normalize_RewritesAgentAndVerificationBlocks()
    {
        var workflow = new Workflow("Models") { Title = "Models" };
        var section = new Section("Body", workflow);
        var agent = new Block(BlockTypes.Agent, section, "agent");
        ((AgentProperties)agent.Properties).Model = BEDROCK_SONNET;
        var verifier = new Block(BlockTypes.Verification, section, "verifier");
        ((VerificationProperties)verifier.Properties).Model = EU_SONNET;
        _ = new Block(BlockTypes.AiQuery, section, "query");

        int changed = AgentBlockModelNormalizer.Normalize(workflow, _bedrockRuntime);

        Assert.Equal(2, changed);
        Assert.Equal(NEUTRAL_SONNET, ((AgentProperties)agent.Properties).Model);
        Assert.Equal(NEUTRAL_SONNET, ((VerificationProperties)verifier.Properties).Model);
    }

    [Fact]
    public void Normalize_LeavesAModelWithNoListedFormAsWritten()
    {
        var workflow = new Workflow("Models") { Title = "Models" };
        var agent = new Block(BlockTypes.Agent, new Section("Body", workflow), "agent");
        ((AgentProperties)agent.Properties).Model = BEDROCK_OPUS;

        int changed = AgentBlockModelNormalizer.Normalize(workflow, _bedrockRuntime);

        Assert.Equal(0, changed);
        Assert.Equal(BEDROCK_OPUS, ((AgentProperties)agent.Properties).Model);
    }

    [Fact]
    public void Normalize_SkipsBlocksInsideASharedComponentLink()
    {
        var workflow = new Workflow("Models") { Title = "Models" };
        var plainSection = new Section("Plain", workflow);
        var linkedBlock = new Block(BlockTypes.Agent, plainSection, "linked_block") { SharedComponentId = "component-block" };
        ((AgentProperties)linkedBlock.Properties).Model = BEDROCK_SONNET;
        var linkedSection = new Section("Linked", workflow) { SharedComponentId = "component-section" };
        var blockInLinkedSection = new Block(BlockTypes.Agent, linkedSection, "section_member");
        ((AgentProperties)blockInLinkedSection.Properties).Model = BEDROCK_SONNET;

        int changed = AgentBlockModelNormalizer.Normalize(workflow, _bedrockRuntime);

        Assert.Equal(0, changed);
        Assert.Equal(BEDROCK_SONNET, ((AgentProperties)linkedBlock.Properties).Model);
        Assert.Equal(BEDROCK_SONNET, ((AgentProperties)blockInLinkedSection.Properties).Model);
    }
}
