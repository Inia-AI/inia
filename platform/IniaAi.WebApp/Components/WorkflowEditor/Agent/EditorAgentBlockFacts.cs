using System.Globalization;
using IniaAi.Common.Extensions;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.WebApp.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <param name="Label">What the fact is, such as "Model".</param>
public sealed record EditorAgentBlockFact(string Label, string Value);

/// <summary>
/// A block's essentials in plain words, for the drawer a block link opens: the start of its
/// instructions, and its model, effort and thinking settings, always reported together.
/// </summary>
public static class EditorAgentBlockFacts
{
    public const int EXCERPT_CHARS = 400;
    public const string MODEL = "Model";
    public const string EFFORT = "Effort";
    public const string THINKING = "Thinking";

    private const string ELLIPSIS = "…";
    private const string DEFAULT_EFFORT = "Not set, so the model's default";
    private const string THINKING_OFF = "Off";
    private const string AGENT_THINKING = "Decided by the agent";
    private const string ADVANCED_MODEL = "The advanced model (the default)";
    private const string SIMPLE_MODEL = "The simple model (the default)";
    private const string ADVANCED_AGENT_MODEL = "The advanced model";
    private const string STANDARD_AGENT_MODEL = "The standard model";

    /// <summary>The start of the block's instructions to the model; null for a block without any.</summary>
    public static string? InstructionsExcerpt(Block block)
    {
        string? instructions = block.Properties switch
        {
            AiQueryProperties ai => Blank(ai.UserPromptTemplate) ?? Blank(ai.SystemPromptTemplate),
            IAgentRunProperties agent => Blank(agent.PromptTemplate) ?? Blank(agent.SystemPromptTemplate),
            _ => null,
        };

        return instructions is null ? null : Excerpt(instructions.Trim());
    }

    /// <summary>The model settings; empty for a block that calls no model.</summary>
    /// <param name="agentTiers">The organization's agent model tiers, which name the model an agent-family block on its tier runs.</param>
    public static IReadOnlyList<EditorAgentBlockFact> ModelSettings(Block block, AgentModelTiers? agentTiers = null)
    {
        return ModelSettings(block.Properties, agentTiers);
    }

    /// <summary>
    /// The model settings a block has once the proposed values of its changed fields replace its
    /// own: each value is a field's JSON, keyed by the field's name in <c>Properties</c>.
    /// </summary>
    public static IReadOnlyList<EditorAgentBlockFact> ModelSettingsWith(
        Block block,
        IReadOnlyDictionary<string, string?> proposed,
        AgentModelTiers? agentTiers = null)
    {
        var copy = (BlockProperties)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(block.Properties), block.Properties.GetType())!;
        var values = new JObject();
        foreach ((string field, string? json) in proposed)
        {
            values[field] = json is null ? JValue.CreateNull() : JToken.Parse(json);
        }

        JsonConvert.PopulateObject(values.ToString(), copy);
        return ModelSettings(copy, agentTiers);
    }

    private static IReadOnlyList<EditorAgentBlockFact> ModelSettings(BlockProperties properties, AgentModelTiers? agentTiers)
    {
        return properties switch
        {
            AiQueryProperties ai =>
            [
                new(MODEL, Blank(ai.ModelIdOverride) is { } modelId
                    ? AgentModelLabels.Describe(modelId)
                    : ai.ShouldUseAdvancedModel ? ADVANCED_MODEL : SIMPLE_MODEL),
                new(EFFORT, ai.Effort?.GetDescription() ?? DEFAULT_EFFORT),
                new(THINKING, ai.ShouldUseThinkingMode
                    ? string.Create(CultureInfo.InvariantCulture, $"On, up to {ai.ThinkingBudget:N0} tokens")
                    : THINKING_OFF),
            ],
            IAgentRunProperties agent =>
            [
                new(MODEL, AgentModel(agent, agentTiers)),
                new(EFFORT, Blank(agent.Effort) is { } effort ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(effort) : DEFAULT_EFFORT),
                new(THINKING, AGENT_THINKING),
            ],
            _ => [],
        };
    }

    private static string AgentModel(IAgentRunProperties agent, AgentModelTiers? agentTiers)
    {
        if (Blank(agent.Model) is { } model)
        {
            return AgentModelLabels.Describe(model);
        }

        string tier = agent.ShouldUseAdvancedModel ? ADVANCED_AGENT_MODEL : STANDARD_AGENT_MODEL;
        return agentTiers is null
            ? tier
            : $"{tier}, {AgentModelLabels.Describe(agentTiers.TierModelFor(agent.ShouldUseAdvancedModel))}";
    }

    private static string Excerpt(string text)
    {
        if (text.Length <= EXCERPT_CHARS)
        {
            return text;
        }

        int cut = text.LastIndexOf(' ', EXCERPT_CHARS);
        if (cut < EXCERPT_CHARS / 2)
        {
            cut = EXCERPT_CHARS;
        }

        return text[..cut].TrimEnd() + ELLIPSIS;
    }

    private static string? Blank(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
