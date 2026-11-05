using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using Newtonsoft.Json;
using System.Reflection;

namespace IniaAi.Domain.Tests.Data.Workflows;

public class ApplySharedDefinitionTests
{
    [Theory]
    [InlineData(true, null)]
    [InlineData(null, true)]
    [InlineData(false, true)]
    public void BlockGraft_KeepsTheCopysOwnFailureSetting(bool? copySetting, bool? definitionSetting)
    {
        var scratchSection = new Section("scratch", new Workflow("scratch"));
        var definition = new Block(BlockTypes.AiQuery, scratchSection, "slot_tag")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Definition prompt", IsCritical = definitionSetting },
        };
        var section = new Section("s", new Workflow("consumer")) { Title = "s" };
        var instance = new Block(BlockTypes.AiQuery, section, "mine")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Old prompt", IsCritical = copySetting },
        };

        instance.ApplySharedDefinition(definition);

        Assert.Equal("Definition prompt", ((AiQueryProperties)instance.Properties).UserPromptTemplate);
        Assert.Equal(copySetting, instance.Properties.IsCritical);
        Assert.Equal(definitionSetting, definition.Properties.IsCritical);
    }

    [Fact]
    public void BlockGraft_CopiesSharedContent_PreservesInstanceFields()
    {
        var scratch = new Workflow("scratch");
        var scratchSection = new Section("scratch", scratch);
        var definition = new Block(BlockTypes.AiQuery, scratchSection, "slot_tag")
        {
            Name = "Definition name",
            Description = "Definition description",
            OutputTitleTemplate = "Definition title",
            DataOutputName = "definition_data",
            Properties = new AiQueryProperties { UserPromptTemplate = "Definition prompt" },
        };
        _ = new TextInput(definition, "slot_input", "definition value");
        definition.SupportedDocumentClasses.Add("Invoice");

        var workflow = new Workflow("consumer");
        var section = new Section("s", workflow) { Title = "s" };
        _ = new Block(BlockTypes.AiQuery, section, "filler");
        var instance = new Block(BlockTypes.AiQuery, section, "mine")
        {
            Name = "Old name",
            OutputTitleTemplate = "Old output title",
            DataOutputName = "my_data",
            Properties = new AiQueryProperties { UserPromptTemplate = "Old prompt" },
        };
        _ = new TextInput(instance, "old_input", "old value");
        instance.SupportedDocumentClasses.Add("Contract");
        instance.SharedComponentId = "component-1";
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = new string('a', 64);
        instance.SharedComponentBindings = new Dictionary<string, string> { ["slot_tag"] = "mine" };
        string stableBlockId = instance.StableBlockId!;

        instance.ApplySharedDefinition(definition);

        Assert.Equal("Old name", instance.Name);
        Assert.Equal("Old output title", instance.OutputTitleTemplate);
        Assert.Equal("my_data", instance.DataOutputName);
        Assert.Equal("Definition description", instance.Description);
        Assert.Equal("Definition prompt", ((AiQueryProperties)instance.Properties).UserPromptTemplate);
        TextInput input = Assert.Single(instance.TextInputs);
        Assert.Equal("slot_input", input.ReplacementTag);
        Assert.Equal("definition value", input.Value);

        Assert.Equal("mine", instance.ReplacementTag);
        Assert.Equal(1, instance.Order);
        Assert.Equal(["Invoice"], instance.SupportedDocumentClasses);
        definition.SupportedDocumentClasses.Add("MutatedAfterGraft");
        Assert.Equal(["Invoice"], instance.SupportedDocumentClasses);
        Assert.Equal(stableBlockId, instance.StableBlockId);
        Assert.Equal("component-1", instance.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, instance.SharedComponentLinkState);
        Assert.Equal(new string('a', 64), instance.SharedComponentVersionHash);
        Assert.Equal("mine", instance.SharedComponentBindings!["slot_tag"]);
        Assert.Same(section, instance.Section);

        ((AiQueryProperties)definition.Properties).UserPromptTemplate = "Mutated after graft";
        Assert.Equal("Definition prompt", ((AiQueryProperties)instance.Properties).UserPromptTemplate);
    }

    [Fact]
    public void SectionGraft_CopiesScalars_PreservesIdentityAndBlocks()
    {
        var scratch = new Workflow("scratch");
        var definition = new Section("Definition name", scratch)
        {
            Title = "Definition title",
            Description = "Definition description",
            TextInputTemplate = "Fill {slot_tag}",
            Configuration = new SectionConfiguration(),
        };
        _ = new Block(BlockTypes.AiQuery, definition, "must_not_land");
        definition.Properties = new SectionProperties { IsSeparator = true, ExcludeFromWordExport = true };

        var workflow = new Workflow("consumer");
        _ = new Section("filler", workflow) { Title = "filler" };
        var instance = new Section("Old name", workflow)
        {
            Title = "Old title",
            GroupColor = GroupColor.Color2,
        };
        var blockA = new Block(BlockTypes.AiQuery, instance, "a");
        var blockB = new Block(BlockTypes.AiQuery, instance, "b");
        instance.SharedComponentId = "component-1";
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = new string('a', 64);
        instance.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "a" };
        string stableSectionId = instance.StableSectionId!;

        instance.ApplySharedDefinition(definition);

        Assert.Equal("Old name", instance.Name);
        Assert.Equal("Old title", instance.Title);
        Assert.Equal(GroupColor.Color2, instance.GroupColor);
        Assert.Equal("Definition description", instance.Description);
        Assert.Equal("Fill {slot_tag}", instance.TextInputTemplate);

        Assert.Equal(1, instance.Order);
        Assert.Equal(stableSectionId, instance.StableSectionId);
        Assert.Equal("component-1", instance.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, instance.SharedComponentLinkState);
        Assert.Equal(new string('a', 64), instance.SharedComponentVersionHash);
        Assert.Equal("a", instance.SharedComponentBindings!["slot"]);
        Assert.Same(workflow, instance.Workflow);
        Assert.Equal(2, instance.Blocks.Count);
        Assert.Same(blockA, instance.Blocks.OrderBy(b => b.Order).First());
        Assert.Same(blockB, instance.Blocks.OrderBy(b => b.Order).Last());

        Assert.True(instance.Properties.IsSeparator);
        Assert.True(instance.Properties.ExcludeFromWordExport);
        Assert.NotSame(definition.Properties, instance.Properties);
        Assert.NotNull(instance.Configuration);
        Assert.NotSame(definition.Configuration, instance.Configuration);
        definition.Properties.IsSeparator = false;
        Assert.True(instance.Properties.IsSeparator);
    }

    [Fact]
    public void SectionGraft_SlotStates_InstanceKept_DefinitionsNeverLand()
    {
        var scratch = new Workflow("scratch");
        var definition = new Section("Definition name", scratch)
        {
            SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
            {
                ["stray"] = SharedComponentSlotState.BrokenOut,
            },
        };

        var workflow = new Workflow("consumer");
        var instance = new Section("Old name", workflow);
        instance.SharedComponentId = "component-1";
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["kyc"] = SharedComponentSlotState.Hidden,
        };

        instance.ApplySharedDefinition(definition);

        Assert.Equal(
            SharedComponentSlotState.Hidden,
            Assert.Single(instance.SharedComponentSlotStates!).Value);
        Assert.Equal("kyc", Assert.Single(instance.SharedComponentSlotStates!).Key);
    }

    [Fact]
    public void SectionGraft_DefinitionEntries_NeverLandOnInstance()
    {
        var scratch = new Workflow("scratch");
        var definition = new Section("Definition name", scratch)
        {
            Entries =
            [
                new SectionEntry
                {
                    SlotTag = "slot",
                    Kind = SectionEntryKind.ComponentRef,
                    SharedComponentId = "block-component",
                },
            ],
        };

        var workflow = new Workflow("consumer");
        var instance = new Section("Old name", workflow);
        instance.SharedComponentId = "component-1";
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;

        instance.ApplySharedDefinition(definition);

        Assert.Null(instance.Entries);
    }

    [Fact]
    public void BlockSerializedMembers_PartitionedExplicitly()
    {
        string[] sharedCopied =
        [
            nameof(Block.Name),
            nameof(Block.OutputTitleTemplate),
            nameof(Block.Description),
            nameof(Block.Type),
            nameof(Block.IsOutputSource),
            nameof(Block.ShouldUpdateCounterpartyWithResult),
            nameof(Block.RunsAlone),
            nameof(Block.CounterpartyResultType),
            nameof(Block.UpdateCounterpartyPropertyKey),
            nameof(Block.ShouldDisplayInLogs),
            nameof(Block.Properties),
            nameof(Block.RegressionSchema),
            nameof(Block.RagSettings),
            nameof(Block.TextInputs),
            nameof(Block.SupportedDocumentClasses),
        ];
        string[] instancePreserved =
        [
            nameof(Block.ReplacementTag),
            nameof(Block.DataOutputName),
            nameof(Block.Order),
            nameof(Block.StableBlockId),
            nameof(Block.SharedComponentId),
            nameof(Block.SharedComponentLinkState),
            nameof(Block.SharedComponentVersionHash),
            nameof(Block.SharedComponentBindings),
            nameof(Block.Id),
            nameof(Block.CreatedAt),
            nameof(Block.UpdatedAt),
            nameof(Block.UpdatedByUserId),
            nameof(Block.IsDeleted),
            nameof(Block.DeletedAt),
            nameof(Block.DeletedByUserId),
        ];
        string[] executionExcluded =
        [
            nameof(Block.ExecutionState),
            nameof(Block.BlockResponse),
        ];

        AssertPartition(typeof(Block), sharedCopied, instancePreserved, executionExcluded);
    }

    [Fact]
    public void SectionSerializedMembers_PartitionedExplicitly()
    {
        string[] sharedCopied =
        [
            nameof(Section.Name),
            nameof(Section.Title),
            nameof(Section.Description),
            nameof(Section.GroupColor),
            nameof(Section.TextInputTemplate),
            nameof(Section.ParameterizedConfig),
            nameof(Section.ExpandedFromParameterizedKey),
            nameof(Section.Properties),
            nameof(Section.Configuration),
        ];
        string[] instancePreserved =
        [
            nameof(Section.Order),
            nameof(Section.StableSectionId),
            nameof(Section.SharedComponentId),
            nameof(Section.SharedComponentLinkState),
            nameof(Section.SharedComponentVersionHash),
            nameof(Section.SharedComponentBindings),
            nameof(Section.SharedComponentSlotStates),
            nameof(Section.Entries),
            nameof(Section.Blocks),
            nameof(Section.Id),
            nameof(Section.CreatedAt),
            nameof(Section.UpdatedAt),
            nameof(Section.UpdatedByUserId),
            nameof(Section.IsDeleted),
            nameof(Section.DeletedAt),
            nameof(Section.DeletedByUserId),
        ];
        string[] executionExcluded =
        [
            nameof(Section.ExecutionState),
        ];

        AssertPartition(typeof(Section), sharedCopied, instancePreserved, executionExcluded);
    }

    private static void AssertPartition(
        Type entityType,
        string[] sharedCopied,
        string[] instancePreserved,
        string[] executionExcluded)
    {
        string[] serialized = [.. entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0
                && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(p => p.Name)];

        string[] claimed = [.. sharedCopied, .. instancePreserved, .. executionExcluded];

        Assert.Equal(claimed.Length, claimed.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal([.. claimed.Order()], [.. serialized.Order()]);
    }
}
