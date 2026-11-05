using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Samples.WordCount;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class BlockTypeVisualsTests
{
    private static readonly InstalledBlockTypes _everyModule = new(ModuleBlockTypes.All);
    private static readonly InstalledBlockTypes _noModule = new([]);
    private static readonly InstalledBlockTypes _everyModuleAndTheSample = new([.. ModuleBlockTypes.All, WordCountProperties.TYPE]);
    private const BlockTypes TYPE_NO_REGISTRATION_KNOWS = (BlockTypes)4242;

    public static TheoryData<BlockTypes> SelectableTypes()
    {
        return [.. BlockTypeVisuals.SelectableFor(agentRuntimeEnabled: true, _everyModuleAndTheSample, BlockTypes.AiQuery)];
    }

    [Theory]
    [MemberData(nameof(SelectableTypes))]
    public void Label_ForSelectableType_DoesNotFallBackToEnumName(BlockTypes type)
    {
        string label = BlockTypeVisuals.Label(type);

        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.NotEqual(type.ToString(), label);
    }

    [Theory]
    [MemberData(nameof(SelectableTypes))]
    public void ShortLabel_ForSelectableType_DoesNotFallBackToEnumName(BlockTypes type)
    {
        string shortLabel = BlockTypeVisuals.ShortLabel(type);

        Assert.False(string.IsNullOrWhiteSpace(shortLabel));
        Assert.NotEqual(type.ToString(), shortLabel);
    }

    [Fact]
    public void Selectable_HasNoDuplicates()
    {
        Assert.Equal(BlockTypeVisuals.Selectable.Count, BlockTypeVisuals.Selectable.Distinct().Count());
    }

    [Fact]
    public void Selectable_AgentImmediatelyFollowsAiQuery()
    {
        int aiQueryIndex = BlockTypeVisuals.Selectable.ToList().IndexOf(BlockTypes.AiQuery);

        Assert.Equal(aiQueryIndex + 1, BlockTypeVisuals.Selectable.ToList().IndexOf(BlockTypes.Agent));
    }

    [Fact]
    public void Selectable_VerificationImmediatelyFollowsAgent()
    {
        int agentIndex = BlockTypeVisuals.Selectable.ToList().IndexOf(BlockTypes.Agent);

        Assert.Equal(agentIndex + 1, BlockTypeVisuals.Selectable.ToList().IndexOf(BlockTypes.Verification));
    }

    [Fact]
    public void SelectableFor_AgentRuntimeEnabled_IncludesTheAgentFamily()
    {
        IReadOnlyList<BlockTypes> selectable = BlockTypeVisuals.SelectableFor(agentRuntimeEnabled: true, _everyModule, BlockTypes.AiQuery);

        Assert.Contains(BlockTypes.Agent, selectable);
        Assert.Contains(BlockTypes.Verification, selectable);
        Assert.Equal(BlockTypeVisuals.Selectable.Count, selectable.Count);
    }

    [Fact]
    public void SelectableFor_AgentRuntimeDisabled_ExcludesTheAgentFamilyOnly()
    {
        IReadOnlyList<BlockTypes> selectable = BlockTypeVisuals.SelectableFor(agentRuntimeEnabled: false, _everyModule, BlockTypes.AiQuery);

        Assert.DoesNotContain(BlockTypes.Agent, selectable);
        Assert.DoesNotContain(BlockTypes.Verification, selectable);
        Assert.Equal(BlockTypeVisuals.Selectable.Count - 2, selectable.Count);
        Assert.All(
            BlockTypeVisuals.Selectable.Where(t => t is not (BlockTypes.Agent or BlockTypes.Verification)),
            t => Assert.Contains(t, selectable));
    }

    [Fact]
    public void SelectableFor_WithoutTheModules_OffersNoModuleType()
    {
        IReadOnlyList<BlockTypes> selectable =
            BlockTypeVisuals.SelectableFor(agentRuntimeEnabled: true, _noModule, BlockTypes.AiQuery);

        Assert.All(ModuleBlockTypes.All, type => Assert.DoesNotContain(type, selectable));
        Assert.Equal(BlockTypeVisuals.Selectable.Count - ModuleBlockTypes.All.Count, selectable.Count);
    }

    [Theory]
    [InlineData(BlockTypes.Json2Xlsx)]
    [InlineData(BlockTypes.PeerFinancials)]
    [InlineData(BlockTypes.Verification)]
    public void SelectableFor_WithoutTheModules_OffersAModuleTypeOnlyToABlockThatHasIt(BlockTypes currentType)
    {
        IReadOnlyList<BlockTypes> selectable =
            BlockTypeVisuals.SelectableFor(agentRuntimeEnabled: true, _noModule, currentType);

        Assert.Equal([currentType], selectable.Where(ModuleBlockTypes.Contains));
    }

    [Fact]
    public void SelectableFor_OffersAnInstalledExtensionType_AfterThePlatformTypes()
    {
        IReadOnlyList<BlockTypes> selectable =
            BlockTypeVisuals.SelectableFor(agentRuntimeEnabled: true, _everyModuleAndTheSample, BlockTypes.AiQuery);

        Assert.Equal(WordCountProperties.TYPE, selectable[^1]);
    }

    [Fact]
    public void SelectableFor_WithoutTheExtension_OffersItsTypeOnlyToABlockThatHasIt()
    {
        Assert.DoesNotContain(
            WordCountProperties.TYPE,
            BlockTypeVisuals.SelectableFor(agentRuntimeEnabled: true, _everyModule, BlockTypes.AiQuery));
        Assert.Contains(
            WordCountProperties.TYPE,
            BlockTypeVisuals.SelectableFor(agentRuntimeEnabled: true, _everyModule, WordCountProperties.TYPE));
    }

    [Fact]
    public void SelectableFor_ABlockOfATypeNoRegistrationKnows_KeepsItsTypeInTheList()
    {
        IReadOnlyList<BlockTypes> selectable =
            BlockTypeVisuals.SelectableFor(agentRuntimeEnabled: true, _everyModule, TYPE_NO_REGISTRATION_KNOWS);

        Assert.Equal(TYPE_NO_REGISTRATION_KNOWS, selectable[^1]);
    }

    [Fact]
    public void ShortLabel_ForATypeNoRegistrationKnows_IsShort()
    {
        Assert.Equal("Unavailable", BlockTypeVisuals.ShortLabel(TYPE_NO_REGISTRATION_KNOWS));
    }

    [Fact]
    public void ShortLabel_ForAnExtensionType_IsTheShortLabelItRegistered()
    {
        Assert.Equal("Words", BlockTypeVisuals.ShortLabel(WordCountProperties.TYPE));
    }
}
