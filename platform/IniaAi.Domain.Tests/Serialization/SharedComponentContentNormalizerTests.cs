using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Serialization;

public class SharedComponentContentNormalizerTests
{
    private const string DROPPED_BLOCK_FIELD = "ShouldExcludeBlockOutputFromCitationSources";

    private readonly SharedComponentContentNormalizer _normalizer = new(new WorkflowCanonicalizer());

    private static Block BlockWithPrompt(Section section, string tag, string prompt)
    {
        return new Block(BlockTypes.AiQuery, section, tag)
        {
            Name = "shared name",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    private static Section SectionWithBlock(Workflow workflow, string title, string tag, string prompt)
    {
        var section = new Section("shared section name", workflow) { Title = title };
        _ = BlockWithPrompt(section, tag, prompt);

        return section;
    }

    [Fact]
    public void StoredHashes_BlockVersionStoredWithAFieldTheModelDropped_EqualTheCopyWithoutIt()
    {
        var section = new Section("s", new Workflow("w")) { Title = "s" };
        Block copy = BlockWithPrompt(section, "tag", "same prompt");
        var stored = JObject.Parse(_normalizer.ToDefinitionJson(copy));
        stored[DROPPED_BLOCK_FIELD] = true;

        Assert.Equal(_normalizer.ComputeContentHash(copy), _normalizer.ComputeStoredContentHash(stored.ToString()));
        Assert.Equal(
            _normalizer.ComputeSignatureInsensitiveContentHash(copy, bindings: null),
            _normalizer.ComputeStoredSignatureInsensitiveContentHash(stored.ToString()));
    }

    [Fact]
    public void StoredHashes_SectionVersionStoredWithAFieldTheModelDropped_EqualTheCopyWithoutIt()
    {
        Section copy = SectionWithBlock(new Workflow("w"), "Currency", "fx", "same prompt");
        var stored = JObject.Parse(_normalizer.ToDefinitionJson(copy));
        foreach (JObject block in ((JArray)stored[nameof(Section.Blocks)]!).OfType<JObject>())
        {
            block[DROPPED_BLOCK_FIELD] = true;
        }

        Assert.Equal(_normalizer.ComputeContentHash(copy), _normalizer.ComputeStoredContentHash(stored.ToString()));
        Assert.Equal(
            _normalizer.ComputeSignatureInsensitiveContentHash(copy, bindings: null),
            _normalizer.ComputeStoredSignatureInsensitiveContentHash(stored.ToString()));
    }

    [Fact]
    public void StoredHashes_VersionWithARealEdit_StillDiffer()
    {
        var section = new Section("s", new Workflow("w")) { Title = "s" };
        Block copy = BlockWithPrompt(section, "tag", "copy prompt");
        string storedDefinition = _normalizer.ToDefinitionJson(BlockWithPrompt(section, "other", "library prompt"));

        Assert.NotEqual(_normalizer.ComputeContentHash(copy), _normalizer.ComputeStoredContentHash(storedDefinition));
        Assert.NotEqual(
            _normalizer.ComputeSignatureInsensitiveContentHash(copy, bindings: null),
            _normalizer.ComputeStoredSignatureInsensitiveContentHash(storedDefinition));
    }

    [Fact]
    public void RebuildDefinitionJson_BoundBlockDefinitionWrittenToday_ComesBackUnchanged()
    {
        var section = new Section("s", new Workflow("w")) { Title = "s" };
        Block copy = BlockWithPrompt(section, "risk_x", "Read {{intro_x}} and rate the risk");
        var bindings = new Dictionary<string, string> { ["intro"] = "intro_x" };
        string definition = _normalizer.ToDefinitionJson(copy, bindings);

        Assert.Equal(definition, _normalizer.RebuildDefinitionJson(definition));
    }

    [Fact]
    public void RebuildDefinitionJson_SectionDefinitionWithEntriesAndBindingsWrittenToday_ComesBackUnchanged()
    {
        var workflow = new Workflow("consumer");
        Section instance = SectionWithBlock(workflow, "Shared Title", "intro_x", "intro prompt");
        Block linked = BlockWithPrompt(instance, "risk_x", "risk prompt");
        linked.SharedComponentId = "block-component";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentBindings = new Dictionary<string, string> { ["b_input"] = "intro_x" };
        var sectionBindings = new Dictionary<string, string>
        {
            ["intro"] = "intro_x",
            ["risk"] = "risk_x",
        };
        string definition = _normalizer.ToDefinitionJson(instance, sectionBindings);

        Assert.Contains($"\"{nameof(Section.Entries)}\"", definition);
        Assert.Equal(definition, _normalizer.RebuildDefinitionJson(definition));
    }

    [Fact]
    public void RebuildDefinitionJson_BlockWithSignatureFieldsWrittenToday_ComesBackUnchanged()
    {
        var section = new Section("s", new Workflow("w")) { Title = "s" };
        Block copy = BlockWithPrompt(section, "signer", "Who signs?");
        ((AiQueryProperties)copy.Properties).SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["director_name"] = new("Director", "Who signs", "N/A"),
        };
        string definition = _normalizer.ToDefinitionJson(copy);

        Assert.Contains("director_name", definition);
        Assert.Equal(definition, _normalizer.RebuildDefinitionJson(definition));
    }

    [Fact]
    public void RebuildDefinitionJson_ParameterizedSectionWrittenToday_ComesBackUnchanged()
    {
        Section copy = SectionWithBlock(new Workflow("w"), "Per fund", "nav", "Read the NAV");
        copy.ParameterizedConfig = JsonConvert.SerializeObject(new ParameterizedSectionConfig(
            [new SectionVariant("F2", "Fund 2")]));
        string definition = _normalizer.ToDefinitionJson(copy);

        Assert.Contains(nameof(Section.ParameterizedConfig), definition);
        Assert.Equal(definition, _normalizer.RebuildDefinitionJson(definition));
    }

    [Fact]
    public void RebuildDefinitionJson_SectionOfLibraryReferencesOnlyWrittenToday_ComesBackUnchanged()
    {
        var section = new Section("Checks", new Workflow("consumer")) { Title = "Checks" };
        Block linked = BlockWithPrompt(section, "risk", "risk prompt");
        linked.SharedComponentId = "block-component";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        string definition = _normalizer.ToDefinitionJson(section);

        Assert.Contains(nameof(SectionEntryKind.ComponentRef), definition);
        Assert.Equal(definition, _normalizer.RebuildDefinitionJson(definition));
    }

    [Fact]
    public void BlockHash_IgnoresPerConsumerFields()
    {
        var workflowA = new Workflow("a");
        var sectionA = new Section("sA", workflowA) { Title = "sA" };
        Block a = BlockWithPrompt(sectionA, "tagA", "same prompt");
        a.SupportedDocumentClasses.Add("Contract");

        var workflowB = new Workflow("b");
        var sectionB = new Section("sB", workflowB) { Title = "sB" };
        _ = BlockWithPrompt(sectionB, "filler", "filler prompt");
        Block b = BlockWithPrompt(sectionB, "tagB", "same prompt");
        b.SupportedDocumentClasses.Add("Contract");

        Assert.Equal(
            _normalizer.ComputeContentHash(a),
            _normalizer.ComputeContentHash(b));
    }

    [Fact]
    public void BlockDefinition_CarriesDocumentClassWiring()
    {
        var workflow = new Workflow("a");
        var section = new Section("s", workflow) { Title = "s" };
        Block block = BlockWithPrompt(section, "tag", "prompt");
        block.SupportedDocumentClasses.Add("Contract");

        string definitionJson = _normalizer.ToDefinitionJson(block);

        Assert.Contains("Contract", definitionJson, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockHash_ChangesWithDocumentClassWiring()
    {
        var workflowA = new Workflow("a");
        var sectionA = new Section("sA", workflowA) { Title = "sA" };
        Block wiredToContract = BlockWithPrompt(sectionA, "tagA", "same prompt");
        wiredToContract.SupportedDocumentClasses.Add("Contract");

        var workflowB = new Workflow("b");
        var sectionB = new Section("sB", workflowB) { Title = "sB" };
        Block wiredToReport = BlockWithPrompt(sectionB, "tagB", "same prompt");
        wiredToReport.SupportedDocumentClasses.Add("Report");

        Assert.NotEqual(
            _normalizer.ComputeContentHash(wiredToContract),
            _normalizer.ComputeContentHash(wiredToReport));
    }

    [Fact]
    public void BlockHash_ChangesWithPromptContent()
    {
        var workflow = new Workflow("a");
        var section = new Section("s", workflow) { Title = "s" };
        Block a = BlockWithPrompt(section, "tag", "prompt one");
        Block b = BlockWithPrompt(section, "tag", "prompt two");

        Assert.NotEqual(
            _normalizer.ComputeContentHash(a),
            _normalizer.ComputeContentHash(b));
    }

    [Fact]
    public void BlockHash_IgnoresInstanceLinkFields()
    {
        var workflow = new Workflow("a");
        var section = new Section("s", workflow) { Title = "s" };
        Block plain = BlockWithPrompt(section, "tag", "same prompt");
        Block linked = BlockWithPrompt(section, "tag", "same prompt");
        linked.SharedComponentId = "component";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentVersionHash = new string('a', 64);
        linked.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "tag" };

        Assert.Equal(
            _normalizer.ComputeContentHash(plain),
            _normalizer.ComputeContentHash(linked));
    }

    [Fact]
    public void BlockHash_IgnoresTheDataOutputName()
    {
        var workflow = new Workflow("a");
        var section = new Section("s", workflow) { Title = "s" };
        Block mine = BlockWithPrompt(section, "tag", "same prompt");
        mine.DataOutputName = "facilities";
        Block theirs = BlockWithPrompt(section, "tag", "same prompt");
        theirs.DataOutputName = "credit_lines";

        Assert.Equal(
            _normalizer.ComputeContentHash(mine),
            _normalizer.ComputeContentHash(theirs));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BlockDefinition_LeavesOutTheFailureSetting(bool isCritical)
    {
        var section = new Section("s", new Workflow("a")) { Title = "s" };
        Block plain = BlockWithPrompt(section, "tag", "same prompt");
        Block marked = BlockWithPrompt(section, "tag", "same prompt");
        marked.Properties.IsCritical = isCritical;

        string definition = _normalizer.ToDefinitionJson(marked);

        Assert.DoesNotContain(nameof(BlockProperties.IsCritical), definition);
        Assert.Equal(_normalizer.ToDefinitionJson(plain), definition);
        Assert.Equal(_normalizer.ComputeContentHash(plain), _normalizer.ComputeContentHash(marked));
        Assert.Equal(isCritical, marked.Properties.IsCritical);
    }

    [Fact]
    public void SectionDefinition_LeavesOutTheFailureSettingOfItsBlocks()
    {
        var workflow = new Workflow("a");
        Section plain = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");
        Section marked = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");
        marked.Blocks.Single().Properties.IsCritical = true;

        string definition = _normalizer.ToDefinitionJson(marked);

        Assert.DoesNotContain(nameof(BlockProperties.IsCritical), definition);
        Assert.Equal(_normalizer.ComputeContentHash(plain), _normalizer.ComputeContentHash(marked));
        Assert.True(marked.Blocks.Single().Properties.IsCritical);
    }

    [Fact]
    public void StoredHashes_VersionStoredWithAFailureSetting_EqualTheCopyWithoutIt()
    {
        var section = new Section("s", new Workflow("w")) { Title = "s" };
        Block copy = BlockWithPrompt(section, "tag", "same prompt");
        var stored = JObject.Parse(_normalizer.ToDefinitionJson(copy));
        stored[nameof(Block.Properties)]![nameof(BlockProperties.IsCritical)] = true;

        Assert.Equal(_normalizer.ComputeContentHash(copy), _normalizer.ComputeStoredContentHash(stored.ToString()));
        Assert.DoesNotContain(
            nameof(BlockProperties.IsCritical),
            SharedComponentContentNormalizer.WithoutPerWorkflowSettings(stored.ToString()));
    }

    [Fact]
    public void StoredHashes_VersionWithoutAFailureSetting_KeepsTheHashItWasStoredUnder()
    {
        var section = new Section("s", new Workflow("w")) { Title = "s" };
        string stored = _normalizer.ToDefinitionJson(BlockWithPrompt(section, "tag", "same prompt"));

        Assert.Equal(
            _normalizer.ComputeContentHashOfDefinitionJson(stored),
            _normalizer.ComputeStoredContentHash(stored));
        Assert.Equal(stored, SharedComponentContentNormalizer.WithoutPerWorkflowSettings(stored));
    }

    [Fact]
    public void SectionHash_IgnoresPlacementAndHostWorkflow()
    {
        var workflowA = new Workflow("a");
        Section a = SectionWithBlock(workflowA, "Shared Title", "tag", "prompt");

        var workflowB = new Workflow("b");
        _ = SectionWithBlock(workflowB, "Filler", "fillerTag", "filler");
        Section b = SectionWithBlock(workflowB, "Shared Title", "tag", "prompt");

        Assert.NotEqual(a.Order, b.Order);
        Assert.Equal(
            _normalizer.ComputeContentHash(a),
            _normalizer.ComputeContentHash(b));
    }

    [Fact]
    public void SectionHash_IgnoresPresentation()
    {
        var workflow = new Workflow("a");
        Section a = SectionWithBlock(workflow, "Title One", "tag", "prompt");
        Section b = SectionWithBlock(workflow, "Title Two", "tag", "prompt");
        b.Name = "a different section name";
        b.GroupColor = GroupColor.Color1;

        Assert.Equal(
            _normalizer.ComputeContentHash(a),
            _normalizer.ComputeContentHash(b));
    }

    [Fact]
    public void SectionHash_ChangesWithDescription()
    {
        var workflow = new Workflow("a");
        Section a = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");
        Section b = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");
        b.Description = "a real setting, not a label";

        Assert.NotEqual(
            _normalizer.ComputeContentHash(a),
            _normalizer.ComputeContentHash(b));
    }

    [Fact]
    public void BlockHash_IgnoresPresentation()
    {
        var workflow = new Workflow("a");
        var section = new Section("s", workflow) { Title = "s" };
        Block a = BlockWithPrompt(section, "tag", "same prompt");
        Block b = BlockWithPrompt(section, "tag", "same prompt");
        b.Name = "a different block name";
        b.OutputTitleTemplate = "A Different Output Title";

        Assert.Equal(
            _normalizer.ComputeContentHash(a),
            _normalizer.ComputeContentHash(b));
    }

    [Fact]
    public void DefinitionJson_KeepsPresentation_SoAFreshInsertLandsLabelled()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Risk Pack", "tag", "prompt");
        section.GroupColor = GroupColor.Color1;

        string json = _normalizer.ToDefinitionJson(section);

        Assert.Contains("Risk Pack", json);
        Assert.Contains(nameof(Section.GroupColor), json);
    }

    [Fact]
    public void SectionHash_ChangesWithInnerBlockTag()
    {
        var workflow = new Workflow("a");
        Section a = SectionWithBlock(workflow, "Shared Title", "tagOne", "prompt");
        Section b = SectionWithBlock(workflow, "Shared Title", "tagTwo", "prompt");

        Assert.NotEqual(
            _normalizer.ComputeContentHash(a),
            _normalizer.ComputeContentHash(b));
    }

    [Fact]
    public void SectionHash_WithBindings_ReverseAppliesRenames_MatchingDefinition()
    {
        var definitionWorkflow = new Workflow("def");
        Section definition = SectionWithBlock(
            definitionWorkflow, "Shared Title", "ar_source", "Uses {ar_source} and {ar_source.page}");
        string definitionHash = _normalizer.ComputeContentHash(definition);

        var consumerWorkflow = new Workflow("consumer");
        Section instance = SectionWithBlock(
            consumerWorkflow, "Shared Title", "ar_source", "Uses {ar_source} and {ar_source.page}");
        var bindings = new Dictionary<string, string> { ["ar_source"] = "ar_source_2" };
        instance.ApplyReplacementTagRenames(bindings);

        Assert.NotEqual(definitionHash, _normalizer.ComputeContentHash(instance));
        Assert.Equal(definitionHash, _normalizer.ComputeContentHash(instance, bindings));
    }

    [Fact]
    public void SectionHash_NullAndEmptyBindings_EqualPlainHash()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");

        string plain = _normalizer.ComputeContentHash(section);

        Assert.Equal(plain, _normalizer.ComputeContentHash(section, null));
        Assert.Equal(plain, _normalizer.ComputeContentHash(section, new Dictionary<string, string>()));
    }

    [Fact]
    public void SectionHash_WithBindings_DoesNotMutateTheInput()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "renamed_tag", "Uses {renamed_tag}");
        string before = section.ToJson(pretty: false);

        _ = _normalizer.ComputeContentHash(
            section, new Dictionary<string, string> { ["slot_tag"] = "renamed_tag" });

        Assert.Equal(before, section.ToJson(pretty: false));
    }

    [Fact]
    public void SectionHash_LocalBlockShadowingSlotTag_StaysDivergent()
    {
        var definitionWorkflow = new Workflow("def");
        Section definition = SectionWithBlock(definitionWorkflow, "Shared Title", "alpha", "Uses {alpha}");
        string definitionHash = _normalizer.ComputeContentHash(definition);

        var consumerWorkflow = new Workflow("consumer");
        Section instance = SectionWithBlock(consumerWorkflow, "Shared Title", "alpha", "Uses {alpha}");
        var bindings = new Dictionary<string, string> { ["alpha"] = "alpha_2" };
        instance.ApplyReplacementTagRenames(bindings);
        _ = BlockWithPrompt(instance, "alpha", "local extra block");

        Assert.NotEqual(definitionHash, _normalizer.ComputeContentHash(instance, bindings));
    }

    [Fact]
    public void DefinitionJson_WithBindings_RestoresSlotTags()
    {
        var workflow = new Workflow("consumer");
        Section instance = SectionWithBlock(
            workflow, "Shared Title", "ar_source_2", "Uses {ar_source_2} and {ar_source_2.page}");

        string definition = _normalizer.ToDefinitionJson(
            instance, new Dictionary<string, string> { ["ar_source"] = "ar_source_2" });

        Assert.Contains("ar_source", definition);
        Assert.DoesNotContain("ar_source_2", definition);
    }

    [Fact]
    public void ToDefinitionJson_DoesNotMutateTheInput()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");
        Block block = section.Blocks.Single();
        block.SupportedDocumentClasses.Add("Contract");
        string blockBefore = block.ToJson(pretty: false);
        string sectionBefore = section.ToJson(pretty: false);

        _ = _normalizer.ToDefinitionJson(block);
        _ = _normalizer.ToDefinitionJson(section);

        Assert.Equal(blockBefore, block.ToJson(pretty: false));
        Assert.Equal(sectionBefore, section.ToJson(pretty: false));
    }

    [Fact]
    public void DefinitionJson_OmitsInstanceIdentityAndLinkFields()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");
        section.SharedComponentId = "component";
        Block block = section.Blocks.Single();
        block.SharedComponentId = "component";
        block.SharedComponentVersionHash = new string('a', 64);

        string definition = _normalizer.ToDefinitionJson(section);

        Assert.DoesNotContain("\"SharedComponentId\"", definition);
        Assert.DoesNotContain("\"SharedComponentVersionHash\"", definition);
        Assert.DoesNotContain("\"StableSectionId\"", definition);
        Assert.DoesNotContain("\"StableBlockId\"", definition);
    }

    [Fact]
    public void DefinitionJson_LinkedInner_ComponentIdMovesToEntryLevel_NeverBlockLevel()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "intro", "intro prompt");
        section.SharedComponentId = "section-component";
        Block linked = BlockWithPrompt(section, "risk", "risk prompt");
        linked.SharedComponentId = "block-component";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentVersionHash = new string('a', 64);

        string definition = _normalizer.ToDefinitionJson(section);
        JObject parsed = JObject.Parse(definition);

        Assert.Null(parsed[nameof(Section.SharedComponentId)]);
        var blocks = (JArray?)parsed[nameof(Section.Blocks)];
        Assert.NotNull(blocks);
        JToken inlineBlock = Assert.Single(blocks);
        Assert.Equal("intro", (string?)inlineBlock[nameof(Block.ReplacementTag)]);
        Assert.Null(inlineBlock[nameof(Block.SharedComponentId)]);
        Assert.DoesNotContain("risk prompt", definition);
        Assert.DoesNotContain("\"SharedComponentVersionHash\"", definition);
        Assert.DoesNotContain("\"StableSectionId\"", definition);
        Assert.DoesNotContain("\"StableBlockId\"", definition);

        var entries = (JArray?)parsed[nameof(Section.Entries)];
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        Assert.Equal(nameof(SectionEntryKind.Inline), (string?)entries[0][nameof(SectionEntry.Kind)]);
        Assert.Equal("intro", (string?)entries[0][nameof(SectionEntry.SlotTag)]);
        Assert.Equal(nameof(SectionEntryKind.ComponentRef), (string?)entries[1][nameof(SectionEntry.Kind)]);
        Assert.Equal("risk", (string?)entries[1][nameof(SectionEntry.SlotTag)]);
        Assert.Equal("block-component", (string?)entries[1][nameof(SectionEntry.SharedComponentId)]);
    }

    [Fact]
    public void DefinitionJson_LinkedBetweenInlines_DensifiesInlineOrdersToEntryOrder()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "intro", "intro prompt");
        Block linked = BlockWithPrompt(section, "risk", "risk prompt");
        linked.SharedComponentId = "block-component";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        _ = BlockWithPrompt(section, "summary", "summary prompt");
        string sourceBefore = section.ToJson(pretty: false);

        string definition = _normalizer.ToDefinitionJson(section);
        JObject parsed = JObject.Parse(definition);

        var entries = (JArray)parsed[nameof(Section.Entries)]!;
        Assert.Equal(
            ["intro", "risk", "summary"],
            entries.Select(e => (string?)e[nameof(SectionEntry.SlotTag)]).ToList());

        var blocks = (JArray)parsed[nameof(Section.Blocks)]!;
        Assert.Equal(2, blocks.Count);
        Assert.Equal("intro", (string?)blocks[0][nameof(Block.ReplacementTag)]);
        Assert.Equal(0, (int?)blocks[0][nameof(Block.Order)]);
        Assert.Equal("summary", (string?)blocks[1][nameof(Block.ReplacementTag)]);
        Assert.Equal(1, (int?)blocks[1][nameof(Block.Order)]);

        Assert.Equal(sourceBefore, section.ToJson(pretty: false));
    }

    [Fact]
    public void DefinitionJson_BrokenOutInner_StaysInline_NoEntriesEmitted()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "intro", "intro prompt");
        Block brokenOut = BlockWithPrompt(section, "risk", "risk prompt");
        brokenOut.SharedComponentId = "block-component";
        brokenOut.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;

        string definition = _normalizer.ToDefinitionJson(section);

        Assert.DoesNotContain($"\"{nameof(Section.Entries)}\"", definition);
        Assert.Contains("risk prompt", definition);
    }

    [Fact]
    public void DefinitionJson_WithBindings_ComposesEntryBindingsIntoSlotNamespace()
    {
        var workflow = new Workflow("consumer");
        Section instance = SectionWithBlock(workflow, "Shared Title", "intro_x", "intro prompt");
        Block linked = BlockWithPrompt(instance, "risk_x", "risk prompt");
        linked.SharedComponentId = "block-component";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentBindings = new Dictionary<string, string> { ["b_input"] = "intro_x" };
        var sectionBindings = new Dictionary<string, string>
        {
            ["intro"] = "intro_x",
            ["risk"] = "risk_x",
        };

        string definition = _normalizer.ToDefinitionJson(instance, sectionBindings);
        JObject parsed = JObject.Parse(definition);

        var entries = (JArray)parsed[nameof(Section.Entries)]!;
        JToken refEntry = entries.Single(
            e => (string?)e[nameof(SectionEntry.Kind)] == nameof(SectionEntryKind.ComponentRef));
        Assert.Equal("risk", (string?)refEntry[nameof(SectionEntry.SlotTag)]);
        Assert.Equal("intro", (string?)refEntry[nameof(SectionEntry.Bindings)]!["b_input"]);
    }

    [Fact]
    public void DefinitionJson_EntryBindings_IdentityPairsOmitted()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "intro", "intro prompt");
        Block linked = BlockWithPrompt(section, "risk", "risk prompt");
        linked.SharedComponentId = "block-component";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentBindings = new Dictionary<string, string> { ["intro"] = "intro" };

        string definition = _normalizer.ToDefinitionJson(section);
        JObject parsed = JObject.Parse(definition);

        var entries = (JArray)parsed[nameof(Section.Entries)]!;
        JToken refEntry = entries.Single(
            e => (string?)e[nameof(SectionEntry.Kind)] == nameof(SectionEntryKind.ComponentRef));
        Assert.Null(refEntry[nameof(SectionEntry.Bindings)]);
    }

    [Fact]
    public void DefinitionJson_DerivedAndReparsed_ByteAgree()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "intro", "intro prompt");
        Block linked = BlockWithPrompt(section, "risk", "risk prompt");
        linked.SharedComponentId = "block-component";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentBindings = new Dictionary<string, string> { ["b_input"] = "intro" };
        _ = BlockWithPrompt(section, "summary", "summary prompt");

        string derived = _normalizer.ToDefinitionJson(section);
        Section? reparsed = Section.FromJson(derived);

        Assert.NotNull(reparsed);
        Assert.Equal(derived, _normalizer.ToDefinitionJson(reparsed));
        Assert.Equal(
            _normalizer.ComputeContentHash(section),
            _normalizer.ComputeContentHash(reparsed));
    }

    [Fact]
    public void SectionHash_ChangesWithRefTarget()
    {
        var workflow = new Workflow("a");
        Section a = SectionWithBlock(workflow, "Shared Title", "intro", "intro prompt");
        Block linkedA = BlockWithPrompt(a, "risk", "risk prompt");
        linkedA.SharedComponentId = "component-a";
        linkedA.SharedComponentLinkState = SharedComponentLinkState.Linked;

        Section b = SectionWithBlock(workflow, "Shared Title", "intro", "intro prompt");
        Block linkedB = BlockWithPrompt(b, "risk", "risk prompt");
        linkedB.SharedComponentId = "component-b";
        linkedB.SharedComponentLinkState = SharedComponentLinkState.Linked;

        Assert.NotEqual(
            _normalizer.ComputeContentHash(a),
            _normalizer.ComputeContentHash(b));
    }

    [Fact]
    public void EntryHash_RefEntryNullAndEmptyBindings_HashIdentically()
    {
        var workflow = new Workflow("a");
        Section definition = SectionWithBlock(workflow, "Shared Title", "intro", "intro prompt");
        var withNull = new SectionEntry
        {
            SlotTag = "kyc",
            Kind = SectionEntryKind.ComponentRef,
            SharedComponentId = "component-kyc",
            Bindings = null,
        };
        var withEmpty = new SectionEntry
        {
            SlotTag = "kyc",
            Kind = SectionEntryKind.ComponentRef,
            SharedComponentId = "component-kyc",
            Bindings = new Dictionary<string, string>(),
        };

        Assert.Equal(
            _normalizer.ComputeEntryContentHash(definition, withNull),
            _normalizer.ComputeEntryContentHash(definition, withEmpty));
    }

    [Fact]
    public void IsSectionDefinition_TrueForEntriesOnlySectionDefinition()
    {
        var workflow = new Workflow("a");
        var section = new Section("shared", workflow) { Title = "Shared" };
        Block linked = BlockWithPrompt(section, "risk", "risk prompt");
        linked.SharedComponentId = "block-component";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;

        string definition = _normalizer.ToDefinitionJson(section);

        Assert.DoesNotContain($"\"{nameof(Section.Blocks)}\"", definition);
        Assert.True(SharedComponentContentNormalizer.IsSectionDefinition(definition));
    }

    [Fact]
    public void SectionHash_IgnoresSlotStates()
    {
        var workflow = new Workflow("a");
        Section plain = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");
        Section overridden = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");
        overridden.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["tag"] = SharedComponentSlotState.Hidden,
        };

        Assert.Equal(
            _normalizer.ComputeContentHash(plain),
            _normalizer.ComputeContentHash(overridden));
        Assert.DoesNotContain(
            $"\"{nameof(Section.SharedComponentSlotStates)}\"",
            _normalizer.ToDefinitionJson(overridden));
    }

    [Fact]
    public void DefinitionJson_AllInline_BytesUnchangedFromC2()
    {
        var workflow = new Workflow("a");
        Section section = SectionWithBlock(workflow, "Shared Title", "tag", "prompt");

        Assert.Equal(
            "{\"Blocks\":[{\"Name\":\"shared name\",\"Order\":0,\"Properties\":{\"MaxTokens\":8000,"
            + "\"ShouldUseAdvancedModel\":true,\"ShouldUsePageImages\":false,\"Type\":\"AiQuery\","
            + "\"UserPromptTemplate\":\"prompt\"},\"ReplacementTag\":\"tag\",\"Type\":\"AiQuery\"}],"
            + "\"Name\":\"shared section name\",\"Order\":0,\"Title\":\"Shared Title\"}",
            _normalizer.ToDefinitionJson(section));
    }
}
