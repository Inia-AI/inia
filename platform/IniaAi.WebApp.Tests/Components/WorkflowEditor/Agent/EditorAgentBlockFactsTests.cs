using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Agent;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Agent;

public class EditorAgentBlockFactsTests
{
    private static Block AgentBlock(string? model, bool shouldUseAdvancedModel)
    {
        var block = new Block(BlockTypes.Agent, new Section("Body", new Workflow("facts")), "agent");
        var props = (AgentProperties)block.Properties;
        props.Model = model;
        props.ShouldUseAdvancedModel = shouldUseAdvancedModel;
        return block;
    }

    private static string ModelFact(Block block, AgentModelTiers? tiers = null)
    {
        return EditorAgentBlockFacts.ModelSettings(block, tiers)
            .Single(static fact => fact.Label == EditorAgentBlockFacts.MODEL).Value;
    }

    [Theory]
    [InlineData(true, "The advanced model, Claude Opus 5.5")]
    [InlineData(false, "The standard model, Claude Sonnet 5.5")]
    public void BlankModel_NamesTheTierAndTheModelItRuns(bool shouldUseAdvancedModel, string expected)
    {
        Assert.Equal(expected, ModelFact(AgentBlock(null, shouldUseAdvancedModel), AgentModelTiers.For(new AgentRuntimeSettings())));
    }

    [Fact]
    public void BlankModel_WithoutTiers_NamesTheTierOnly()
    {
        Assert.Equal("The standard model", ModelFact(AgentBlock(null, shouldUseAdvancedModel: false)));
    }

    [Fact]
    public void SetModel_NamesThatModel_WhateverTheTier()
    {
        Assert.Equal(
            "Claude Sonnet 5.5",
            ModelFact(AgentBlock("claude-sonnet-5-5", shouldUseAdvancedModel: true), AgentModelTiers.For(new AgentRuntimeSettings())));
    }

    [Fact]
    public void ProposedTierChange_ShowsTheModelTheBlockWouldRun()
    {
        Block block = AgentBlock(null, shouldUseAdvancedModel: true);

        string after = EditorAgentBlockFacts.ModelSettingsWith(
                block,
                new Dictionary<string, string?> { ["ShouldUseAdvancedModel"] = "false" },
                AgentModelTiers.For(new AgentRuntimeSettings()))
            .Single(static fact => fact.Label == EditorAgentBlockFacts.MODEL).Value;

        Assert.Equal("The standard model, Claude Sonnet 5.5", after);
    }
}
