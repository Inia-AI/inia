using IniaAi.Application.Common.Workflows;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class SharedComponentDefinitionDifferTests
{
    private const string REF_TARGET_ID = "component-1";
    private const string OTHER_TARGET_ID = "component-2";

    private readonly SharedComponentContentNormalizer _normalizer =
        new(new WorkflowCanonicalizer());
    private readonly SharedComponentDefinitionDiffer _differ =
        new(new WorkflowDiffService(), new WorkflowNarrativeGenerator());

    [Fact]
    public void CompareContent_LibraryVersionStoredWithAFailureSetting_ReportsNothing()
    {
        var section = new Section("s", new Workflow("w")) { Title = "s" };
        var block = new Block(BlockTypes.AiQuery, section, "signer")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Who signs?" },
        };
        string copy = _normalizer.ToDefinitionJson(block);
        var stored = Newtonsoft.Json.Linq.JObject.Parse(copy);
        stored[nameof(Block.Properties)]![nameof(AiQueryProperties.IsCritical)] = true;

        WorkflowDiff diff = _differ.CompareContent(SharedComponentKind.Block, stored.ToString(), copy);

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Compare_UnchangedRefDefinition_ReportsNothing()
    {
        string definition = RefDefinition(REF_TARGET_ID);

        WorkflowDiff diff = _differ.Compare(SharedComponentKind.Section, definition, definition);

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Compare_RefEntryInlined_ReportsInlineTransitionOnly()
    {
        string from = RefDefinition(REF_TARGET_ID);
        string to = InlinedDefinition();

        WorkflowDiff diff = _differ.Compare(SharedComponentKind.Section, from, to);

        Assert.Empty(diff.Sections);
        BlockDiff row = Assert.Single(diff.Blocks);
        Assert.Equal("kyc", row.ReplacementTag);
        Assert.Equal(DiffChangeKind.Modified, row.ChangeKind);
        Assert.False(row.IsLibraryLinked);
        FieldDiff transition = Assert.Single(row.FieldDiffs);
        Assert.Equal(SharedComponentDiffFields.ID, transition.FieldName);
        Assert.Equal(REF_TARGET_ID, transition.Before);
        Assert.Null(transition.After);
    }

    [Fact]
    public void Compare_InlineSlotConvertedToRef_ReportsReferenceTransition()
    {
        string from = InlinedDefinition();
        string to = RefDefinition(REF_TARGET_ID);

        WorkflowDiff diff = _differ.Compare(SharedComponentKind.Section, from, to);

        BlockDiff row = Assert.Single(diff.Blocks);
        Assert.Equal("kyc", row.ReplacementTag);
        Assert.Equal(DiffChangeKind.Modified, row.ChangeKind);
        Assert.True(row.IsLibraryLinked);
        FieldDiff transition = Assert.Single(row.FieldDiffs);
        Assert.Equal(SharedComponentDiffFields.ID, transition.FieldName);
        Assert.Null(transition.Before);
        Assert.Equal(REF_TARGET_ID, transition.After);
    }

    [Fact]
    public void Compare_RefRetargeted_ReportsBothIds()
    {
        string from = RefDefinition(REF_TARGET_ID);
        string to = RefDefinition(OTHER_TARGET_ID);

        WorkflowDiff diff = _differ.Compare(SharedComponentKind.Section, from, to);

        BlockDiff row = Assert.Single(diff.Blocks);
        Assert.Equal(DiffChangeKind.Modified, row.ChangeKind);
        FieldDiff transition = Assert.Single(row.FieldDiffs);
        Assert.Equal(SharedComponentDiffFields.ID, transition.FieldName);
        Assert.Equal(REF_TARGET_ID, transition.Before);
        Assert.Equal(OTHER_TARGET_ID, transition.After);
    }

    [Fact]
    public void Compare_RefBindingsChanged_ReportsWiringOnly()
    {
        string from = RefDefinition(
            REF_TARGET_ID, new Dictionary<string, string> { ["rate"] = "intro" });
        string to = RefDefinition(
            REF_TARGET_ID, new Dictionary<string, string> { ["rate"] = "extra" });

        WorkflowDiff diff = _differ.Compare(SharedComponentKind.Section, from, to);

        BlockDiff row = Assert.Single(diff.Blocks);
        Assert.Equal(DiffChangeKind.Modified, row.ChangeKind);
        FieldDiff wiring = Assert.Single(row.FieldDiffs);
        Assert.Equal("SharedComponentBindings", wiring.FieldName);
        Assert.Equal("rate=intro", wiring.Before);
        Assert.Equal("rate=extra", wiring.After);
    }

    [Fact]
    public void Compare_RefAdded_SynthesizesAddedReferenceRow()
    {
        string from = IntroOnlyDefinition();
        string to = RefDefinition(REF_TARGET_ID);

        WorkflowDiff diff = _differ.Compare(SharedComponentKind.Section, from, to);

        BlockDiff row = Assert.Single(diff.Blocks);
        Assert.Equal("kyc", row.ReplacementTag);
        Assert.Equal(DiffChangeKind.Added, row.ChangeKind);
        Assert.True(row.IsLibraryLinked);
        FieldDiff transition = Assert.Single(row.FieldDiffs);
        Assert.Equal(SharedComponentDiffFields.ID, transition.FieldName);
        Assert.Equal(REF_TARGET_ID, transition.After);
    }

    [Fact]
    public void Compare_RefRemoved_SynthesizesRemovedReferenceRow()
    {
        string from = RefDefinition(REF_TARGET_ID);
        string to = IntroOnlyDefinition();

        WorkflowDiff diff = _differ.Compare(SharedComponentKind.Section, from, to);

        BlockDiff row = Assert.Single(diff.Blocks);
        Assert.Equal("kyc", row.ReplacementTag);
        Assert.Equal(DiffChangeKind.Removed, row.ChangeKind);
        FieldDiff transition = Assert.Single(row.FieldDiffs);
        Assert.Equal(SharedComponentDiffFields.ID, transition.FieldName);
        Assert.Equal(REF_TARGET_ID, transition.Before);
    }

    [Fact]
    public void Describe_RetireInliningPair_ReadsSensibly()
    {
        string? narrative = _differ.Describe(
            SharedComponentKind.Section, RefDefinition(REF_TARGET_ID), InlinedDefinition());

        Assert.Equal("Inlined Library block 'component-1' into 'kyc'", narrative);
    }

    [Fact]
    public void Describe_ConvertPair_ReadsSensibly()
    {
        string? narrative = _differ.Describe(
            SharedComponentKind.Section, InlinedDefinition(), RefDefinition(REF_TARGET_ID));

        Assert.Equal("'kyc' now references Library block 'component-1'", narrative);
    }

    [Fact]
    public void Describe_BlockKindPromptEdit_UnchangedByEntriesAwareness()
    {
        string from = BlockDefinition("Who is the counterparty?");
        string to = BlockDefinition("Who is the counterparty, and where?");

        string? narrative = _differ.Describe(SharedComponentKind.Block, from, to);

        Assert.Equal("Changed the prompt in 'Node'", narrative);
    }

    private string RefDefinition(string refTargetId, Dictionary<string, string>? bindings = null)
    {
        Section section = SectionWithIntro();
        section.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = refTargetId,
                Bindings = bindings,
            },
        ];

        return _normalizer.ToDefinitionJson(section);
    }

    private string InlinedDefinition()
    {
        Section section = SectionWithIntro();
        _ = new Block(BlockTypes.AiQuery, section, "kyc")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "KYC v1" },
        };

        return _normalizer.ToDefinitionJson(section);
    }

    private string IntroOnlyDefinition()
    {
        return _normalizer.ToDefinitionJson(SectionWithIntro());
    }

    private string BlockDefinition(string prompt)
    {
        var scratch = new Section("Scratch", new Workflow("Scratch")) { Title = "Scratch" };
        var block = new Block(BlockTypes.AiQuery, scratch, "definition")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };

        return _normalizer.ToDefinitionJson(block);
    }

    private static Section SectionWithIntro()
    {
        var section = new Section("Risk", new Workflow("Scratch")) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, section, "intro")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "Intro prompt" },
        };

        return section;
    }
}
