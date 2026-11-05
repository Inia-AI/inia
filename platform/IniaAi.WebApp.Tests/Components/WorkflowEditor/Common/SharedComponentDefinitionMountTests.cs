using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using ContractsKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

public class SharedComponentDefinitionMountTests
{
    private readonly SharedComponentContentNormalizer _normalizer = new(new WorkflowCanonicalizer());

    [Fact]
    public void BuildViewSection_SectionDefinition_MountsAttachedSection()
    {
        string definitionJson = TwoBlockSectionDefinition();

        Section mounted = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Section, "Risk", definitionJson);

        Assert.NotNull(mounted.Workflow);
        Assert.Contains(mounted, mounted.Workflow!.Sections);
        Assert.Equal("Risk", mounted.Title);
        Assert.Equal(2, mounted.Blocks.Count);
        List<Block> ordered = [.. mounted.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["alpha", "beta"], ordered.Select(b => b.ReplacementTag));
        Assert.Equal("Alpha prompt", ((AiQueryProperties)ordered[0].Properties).UserPromptTemplate);
    }

    [Fact]
    public void BuildViewSection_RefBearingDefinition_MountsInlineBlocksOnly()
    {
        string definitionJson = RefBearingDefinition();

        Section mounted = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Section, "Risk", definitionJson);

        Block inline = Assert.Single(mounted.Blocks);
        Assert.Equal("intro", inline.ReplacementTag);
    }

    [Fact]
    public void BuildViewSection_RefBearingDefinition_CarriesRefEntries()
    {
        Section mounted = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Section, "Risk", RefBearingDefinition());

        List<SectionEntry> refs = [.. mounted.EffectiveEntries()
            .Where(e => e.Kind == SectionEntryKind.ComponentRef)];

        SectionEntry entry = Assert.Single(refs);
        Assert.Equal("kyc", entry.SlotTag);
        Assert.Equal("c-kyc", entry.SharedComponentId);
    }

    [Fact]
    public void BuildViewSection_AllInlineSectionAndBlockKind_CarryNoRefEntries()
    {
        Section section = OneBlockSection();
        string inlineOnly = _normalizer.ToDefinitionJson(section);
        string blockDefinition = _normalizer.ToDefinitionJson(section.Blocks.First());

        Assert.DoesNotContain(
            SharedComponentDefinitionMount.BuildViewSection(ContractsKind.Section, "Risk", inlineOnly).EffectiveEntries(),
            e => e.Kind == SectionEntryKind.ComponentRef);
        Assert.DoesNotContain(
            SharedComponentDefinitionMount.BuildViewSection(ContractsKind.Block, "Alpha", blockDefinition).EffectiveEntries(),
            e => e.Kind == SectionEntryKind.ComponentRef);
    }

    [Fact]
    public void BuildEditSection_SectionScalars_SurviveMountAndCapture()
    {
        var donor = new Workflow("Donor") { Title = "Donor", WorkflowTypeId = "WT" };
        var section = new Section("authored_name", donor)
        {
            Title = "Risk",
            Description = "Section level description",
            TextInputTemplate = "{Counterparty.Name}",
            ParameterizedConfig = "{\"key\":\"value\"}",
        };
        _ = new Block(BlockTypes.AiQuery, section, 0, "alpha")
        {
            Name = "Alpha",
            Properties = new AiQueryProperties { UserPromptTemplate = "Alpha prompt" },
        };
        string definitionJson = _normalizer.ToDefinitionJson(section);

        Section mounted = SharedComponentDefinitionMount.BuildEditSection(
            ContractsKind.Section, "Registry Display Name", definitionJson);

        Assert.Equal("authored_name", mounted.Name);
        Assert.Equal("Section level description", mounted.Description);
        Assert.Equal("{Counterparty.Name}", mounted.TextInputTemplate);
        Assert.Equal("{\"key\":\"value\"}", mounted.ParameterizedConfig);

        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Section, mounted);

        Assert.Equal(definitionJson, captured);
    }

    private static Section OneBlockSection()
    {
        var donor = new Workflow("Donor") { Title = "Donor", WorkflowTypeId = "WT" };
        var section = new Section("Risk", donor) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, section, 0, "alpha")
        {
            Name = "Alpha",
            Properties = new AiQueryProperties { UserPromptTemplate = "Alpha prompt" },
        };

        return section;
    }

    private string TwoBlockSectionDefinition()
    {
        var donor = new Workflow("Donor") { Title = "Donor", WorkflowTypeId = "WT" };
        var section = new Section("Risk", donor) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, section, 0, "alpha")
        {
            Name = "Alpha",
            Properties = new AiQueryProperties { UserPromptTemplate = "Alpha prompt" },
        };
        _ = new Block(BlockTypes.AiQuery, section, 1, "beta")
        {
            Name = "Beta",
            Properties = new AiQueryProperties { UserPromptTemplate = "Beta prompt" },
        };

        return _normalizer.ToDefinitionJson(section);
    }

    private string SignerBlockDefinition()
    {
        var donor = new Workflow("Donor") { Title = "Donor", WorkflowTypeId = "WT" };
        var section = new Section("Host", donor) { Title = "Host" };
        var block = new Block(BlockTypes.AiQuery, section, 0, "signer")
        {
            Name = "Who signs",
            Properties = new AiQueryProperties { UserPromptTemplate = "Who signs the agreement?" },
        };

        return _normalizer.ToDefinitionJson(block);
    }

    private string RefBearingDefinition()
    {
        var donor = new Workflow("Donor") { Title = "Donor", WorkflowTypeId = "WT" };
        var section = new Section("Risk", donor) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, section, 0, "intro")
        {
            Name = "Intro",
            Properties = new AiQueryProperties { UserPromptTemplate = "Intro prompt" },
        };
        section.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = "c-kyc" },
        ];

        return _normalizer.ToDefinitionJson(section);
    }

    [Fact]
    public void BuildViewSection_BlockDefinition_MountsOneBlockScratchSection()
    {
        string definitionJson = SignerBlockDefinition();

        Section mounted = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Block, "Who signs", definitionJson);

        Assert.NotNull(mounted.Workflow);
        Assert.Equal("Who signs", mounted.Title);
        Block landed = Assert.Single(mounted.Blocks);
        Assert.Equal("Who signs", landed.Name);
        Assert.Equal(
            "Who signs the agreement?",
            ((AiQueryProperties)landed.Properties).UserPromptTemplate);
    }

    [Fact]
    public void BuildViewSection_CorruptJson_Throws()
    {
        _ = Assert.Throws<InvalidOperationException>(() =>
            SharedComponentDefinitionMount.BuildViewSection(
                ContractsKind.Section, "Broken", "not json at all"));
    }

    [Fact]
    public void ExtractDefinitionJson_SectionDefinition_RoundTripsByteIdentical()
    {
        string definitionJson = TwoBlockSectionDefinition();

        Section mounted = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Section, "Risk", definitionJson);
        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Section, mounted);

        Assert.Equal(definitionJson, captured);
    }

    [Fact]
    public void ExtractDefinitionJson_BlockDefinition_RoundTripsByteIdentical()
    {
        string definitionJson = SignerBlockDefinition();

        Section mounted = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Block, "Who signs", definitionJson);
        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Block, mounted);

        Assert.Equal(definitionJson, captured);
    }

    [Fact]
    public void ExtractDefinitionJson_RefBearingDefinition_RoundTripsByteIdentical()
    {
        string definitionJson = RefBearingDefinition();

        Section mounted = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Section, "Risk", definitionJson);
        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Section, mounted);

        Assert.Equal(definitionJson, captured);
    }

    [Fact]
    public void ExtractDefinitionJson_BlockPromptEdited_ReflectsEdit()
    {
        string definitionJson = SignerBlockDefinition();

        Section mounted = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Block, "Who signs", definitionJson);
        ((AiQueryProperties)mounted.Blocks.Single().Properties).UserPromptTemplate = "Who countersigns?";
        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Block, mounted);

        Assert.NotEqual(definitionJson, captured);
        Assert.Contains("Who countersigns?", captured);
    }

    [Fact]
    public void ExtractDefinitionJson_BlockKindHostNotSingleBlock_Throws()
    {
        var donor = new Workflow("Donor") { Title = "Donor", WorkflowTypeId = "WT" };
        var host = new Section("Host", donor) { Title = "Host" };
        _ = new Block(BlockTypes.AiQuery, host, 0, "a");
        _ = new Block(BlockTypes.AiQuery, host, 1, "b");

        _ = Assert.Throws<InvalidOperationException>(() =>
            SharedComponentDefinitionMount.ExtractDefinitionJson(ContractsKind.Block, host));
    }

    [Fact]
    public void BuildEditSection_RefBearingDefinition_MaterializesRefAsLinkedBlock()
    {
        Section mounted = SharedComponentDefinitionMount.BuildEditSection(
            ContractsKind.Section, "Risk", RefBearingDefinition());

        Assert.Null(mounted.Entries);
        Block refBlock = Assert.Single(mounted.Blocks, b => b.SharedComponentId is not null);
        Assert.Equal("kyc", refBlock.ReplacementTag);
        Assert.Equal("c-kyc", refBlock.SharedComponentId);
        List<Block> ordered = [.. mounted.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["intro", "kyc"], ordered.Select(b => b.ReplacementTag));
    }

    [Fact]
    public void BuildEditSection_RefBearingDefinition_RoundTripsByteIdentical()
    {
        string definitionJson = RefBearingDefinition();

        Section mounted = SharedComponentDefinitionMount.BuildEditSection(
            ContractsKind.Section, "Risk", definitionJson);
        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Section, mounted);

        Assert.Equal(definitionJson, captured);
    }

    [Fact]
    public void BuildEditSection_InlineSectionDefinition_RoundTripsByteIdentical()
    {
        string definitionJson = TwoBlockSectionDefinition();

        Section mounted = SharedComponentDefinitionMount.BuildEditSection(
            ContractsKind.Section, "Risk", definitionJson);
        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Section, mounted);

        Assert.Equal(definitionJson, captured);
    }

    [Fact]
    public void ComposeRef_InsertedLinkedBlock_CapturesComponentRefEntryInOrder()
    {
        string inlineOnly = _normalizer.ToDefinitionJson(OneBlockSection());

        Section mounted = SharedComponentDefinitionMount.BuildEditSection(
            ContractsKind.Section, "Risk", inlineOnly);
        _ = new Block(BlockTypes.AiQuery, mounted, mounted.Blocks.Count, "kyc")
        {
            SharedComponentId = "c-kyc",
            SharedComponentLinkState = SharedComponentLinkState.Linked,
        };

        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Section, mounted);
        Section recaptured = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Section, "Risk", captured);

        List<SectionEntry> entries = [.. recaptured.EffectiveEntries()];
        Assert.Equal(["alpha", "kyc"], entries.Select(e => e.SlotTag));
        Assert.Equal(
            [SectionEntryKind.Inline, SectionEntryKind.ComponentRef],
            entries.Select(e => e.Kind));
        SectionEntry refEntry = Assert.Single(entries, e => e.Kind == SectionEntryKind.ComponentRef);
        Assert.Equal("c-kyc", refEntry.SharedComponentId);
    }

    [Fact]
    public void ComposeRef_RemovedLinkedBlock_CapturesNoComponentRefEntry()
    {
        Section mounted = SharedComponentDefinitionMount.BuildEditSection(
            ContractsKind.Section, "Risk", RefBearingDefinition());
        Block refBlock = Assert.Single(mounted.Blocks, b => b.SharedComponentId is not null);
        _ = mounted.RemoveBlockAt(refBlock.Order);

        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Section, mounted);
        Section recaptured = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Section, "Risk", captured);

        Assert.DoesNotContain(
            recaptured.EffectiveEntries(),
            e => e.Kind == SectionEntryKind.ComponentRef);
    }

    [Fact]
    public void ComposeRef_SwappedLinkedBlock_CapturesNewTargetSameSlot()
    {
        Section mounted = SharedComponentDefinitionMount.BuildEditSection(
            ContractsKind.Section, "Risk", RefBearingDefinition());
        Block refBlock = Assert.Single(mounted.Blocks, b => b.SharedComponentId is not null);
        refBlock.SharedComponentId = "c-swapped";

        string captured = SharedComponentDefinitionMount.ExtractDefinitionJson(
            ContractsKind.Section, mounted);
        Section recaptured = SharedComponentDefinitionMount.BuildViewSection(
            ContractsKind.Section, "Risk", captured);

        SectionEntry refEntry = Assert.Single(
            recaptured.EffectiveEntries(),
            e => e.Kind == SectionEntryKind.ComponentRef);
        Assert.Equal("kyc", refEntry.SlotTag);
        Assert.Equal("c-swapped", refEntry.SharedComponentId);
    }
}
