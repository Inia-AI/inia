using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Serialization;

/// <summary>
/// Agent and Verification blocks default their model tier as an AiQuery block does: advanced,
/// both on a new block and on a stored block whose JSON predates the field.
/// </summary>
public class AgentModelTierSerializationTests
{
    private const string TIER_FIELD = nameof(IAgentRunProperties.ShouldUseAdvancedModel);
    private const string STORED_MODEL = "claude-sonnet-5-5";

    [Theory]
    [InlineData(BlockTypes.Agent)]
    [InlineData(BlockTypes.Verification)]
    [InlineData(BlockTypes.AiQuery)]
    public void NewBlock_DefaultsToTheAdvancedTier_WithNoModelOverride(BlockTypes type)
    {
        var block = new Block(type, new Section("Body", new Workflow("tiers")), "block");

        Assert.True(TierOf(block.Properties));
        if (block.Properties is IAgentRunProperties agent)
        {
            Assert.Null(agent.Model);
        }
    }

    [Theory]
    [InlineData(BlockTypes.Agent)]
    [InlineData(BlockTypes.Verification)]
    [InlineData(BlockTypes.AiQuery)]
    public void StoredBlockWithoutTheField_ReadsAsTheAdvancedTier(BlockTypes type)
    {
        Section restored = RoundTrip(type, static props => props.Remove(TIER_FIELD));

        Assert.True(TierOf(restored.Blocks.Single().Properties));
    }

    [Theory]
    [InlineData(BlockTypes.Agent)]
    [InlineData(BlockTypes.Verification)]
    public void StandardTierAndStoredModel_SurviveARoundTrip(BlockTypes type)
    {
        Section restored = RoundTrip(type, static props =>
        {
            props[TIER_FIELD] = false;
            props[nameof(IAgentRunProperties.Model)] = STORED_MODEL;
        });

        var agent = (IAgentRunProperties)restored.Blocks.Single().Properties;
        Assert.False(agent.ShouldUseAdvancedModel);
        Assert.Equal(STORED_MODEL, agent.Model);
    }

    private static Section RoundTrip(BlockTypes type, Action<JObject> editProperties)
    {
        var section = new Section("Body", new Workflow("tiers"));
        _ = new Block(type, section, "block");
        JObject json = JObject.Parse(section.ToJson(pretty: false));
        JObject properties = (JObject)json.SelectToken("$..Blocks[0].Properties")!;
        editProperties(properties);

        return Section.FromJson(json.ToString())!;
    }

    private static bool TierOf(BlockProperties properties)
    {
        return properties switch
        {
            AiQueryProperties ai => ai.ShouldUseAdvancedModel,
            IAgentRunProperties agent => agent.ShouldUseAdvancedModel,
            _ => throw new InvalidOperationException($"{properties.GetType().Name} has no model tier"),
        };
    }
}
