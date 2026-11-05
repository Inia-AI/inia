using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class SharedComponentEditorTests
{
    private const string COMPONENT_ID = "component-1";
    private static readonly string _versionHash = new string('a', 64);

    private readonly WorkflowCanonicalizer _canonicalizer = new();
    private readonly SharedComponentContentNormalizer _normalizer;
    private readonly SharedComponentEditor _editor;

    public SharedComponentEditorTests()
    {
        _normalizer = new SharedComponentContentNormalizer(_canonicalizer);
        _editor = new SharedComponentEditor(_canonicalizer, _normalizer);
    }

    [Fact]
    public void ResolveTarget_PathWithGappedOrders_ResolvesByRank_WithoutMutatingOrders()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out Section second);
        first.Order = 0;
        second.Order = 9;

        ErrorOr<SharedComponentTarget> result = _editor.ResolveTarget(workflow, "Sections[1]", null);

        Assert.False(result.IsError);
        Assert.Same(second, result.Value.Section);
        Assert.Equal(9, second.Order);
        Assert.Equal("Sections[1]", result.Value.CanonicalPath);
    }

    [Fact]
    public void ResolveTarget_BlockPath_ResolvesBlockWithParent()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);

        ErrorOr<SharedComponentTarget> result = _editor.ResolveTarget(workflow, "Sections[0].Blocks[1]", null);

        Assert.False(result.IsError);
        Assert.Equal(SharedComponentKind.Block, result.Value.Kind);
        Assert.Equal("s1b2", result.Value.Block!.ReplacementTag);
        Assert.Same(first, result.Value.ParentSection);
    }

    [Fact]
    public void ResolveTarget_StableIds_FindSectionAndBlock()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out Section second);
        Block block = first.Blocks.First();

        ErrorOr<SharedComponentTarget> sectionResult = _editor.ResolveTarget(workflow, null, second.StableSectionId);
        ErrorOr<SharedComponentTarget> blockResult = _editor.ResolveTarget(workflow, null, block.StableBlockId);

        Assert.False(sectionResult.IsError);
        Assert.Same(second, sectionResult.Value.Section);
        Assert.Equal("Sections[1]", sectionResult.Value.CanonicalPath);
        Assert.False(blockResult.IsError);
        Assert.Same(block, blockResult.Value.Block);
        Assert.Equal("Sections[0].Blocks[0]", blockResult.Value.CanonicalPath);
    }

    [Fact]
    public void ResolveTarget_UnknownTarget_NotFoundWithCounts()
    {
        Workflow workflow = TwoSectionWorkflow(out _, out _);

        ErrorOr<SharedComponentTarget> result = _editor.ResolveTarget(workflow, "Sections[7]", null);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.TARGET_NOT_FOUND, result.FirstError.Code);
        Assert.Equal(2, result.FirstError.Metadata!["sectionCount"]);
    }

    [Fact]
    public void ResolveTarget_PathIndexBeyondIntRange_NotFoundInsteadOfThrowing()
    {
        Workflow workflow = TwoSectionWorkflow(out _, out _);

        ErrorOr<SharedComponentTarget> result = _editor.ResolveTarget(
            workflow, "Sections[99999999999999999999]", null);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public void ResolveTarget_BothOrNeitherLocator_Rejected()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);

        ErrorOr<SharedComponentTarget> neither = _editor.ResolveTarget(workflow, null, null);
        ErrorOr<SharedComponentTarget> both = _editor.ResolveTarget(workflow, "Sections[0]", first.StableSectionId);

        Assert.True(neither.IsError);
        Assert.True(both.IsError);
        Assert.Equal(ErrorType.Validation, neither.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.INVALID_TARGET_LOCATOR, neither.FirstError.Code);
        Assert.Contains(SharedComponentErrorCodes.INVALID_TARGET_LOCATOR, both.FirstError.Description);
    }

    [Fact]
    public void ResolveTarget_MalformedPath_ValidationWithLocatorCode()
    {
        Workflow workflow = TwoSectionWorkflow(out _, out _);

        ErrorOr<SharedComponentTarget> result = _editor.ResolveTarget(workflow, "not-a-path", null);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.INVALID_TARGET_LOCATOR, result.FirstError.Code);
    }

    [Fact]
    public void ResolveTarget_StableIdMatchingBothPools_ValidationWithLocatorCode()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        Block block = first.Blocks.First();
        block.StableBlockId = "shared-id";
        first.StableSectionId = "shared-id";

        ErrorOr<SharedComponentTarget> result = _editor.ResolveTarget(workflow, null, "shared-id");

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.INVALID_TARGET_LOCATOR, result.FirstError.Code);
    }

    [Fact]
    public void ValidatePromotable_AlreadyLinkedNode_ConflictWithProvenanceMetadata()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        Block block = first.Blocks.First();
        block.SharedComponentId = "existing-component";
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        SharedComponentTarget target = Resolve(workflow, "Sections[0].Blocks[0]");

        ErrorOr<Success> result = _editor.ValidatePromotable(target);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("existing-component", result.FirstError.Metadata!["existingComponentId"]);
        Assert.Equal("Linked", result.FirstError.Metadata!["linkState"]);
    }

    [Fact]
    public void ValidatePromotable_BrokenOutBlockInAPlainSection_SharesLikeLocalContent()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        Block block = first.Blocks.First();
        block.SharedComponentId = "existing-component";
        block.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;

        ErrorOr<Success> result = _editor.ValidatePromotable(Resolve(workflow, "Sections[0].Blocks[0]"));

        Assert.False(result.IsError);
    }

    [Fact]
    public void ValidatePromotable_BrokenOutBlockInsideALinkedSection_StaysWithLibrarySync()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        first.SharedComponentId = "section-component";
        first.SharedComponentLinkState = SharedComponentLinkState.Linked;
        Block block = first.Blocks.First();
        block.SharedComponentId = "existing-component";
        block.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;

        ErrorOr<Success> result = _editor.ValidatePromotable(Resolve(workflow, "Sections[0].Blocks[0]"));

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.ALREADY_SHARED, result.FirstError.Code);
    }

    [Fact]
    public void ValidatePromotable_SectionWithLinkedInnerBlock_SucceedsAndDerivesRefEntry()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        Block inner = first.Blocks.First();
        inner.SharedComponentId = "nested-component";
        inner.SharedComponentLinkState = SharedComponentLinkState.Linked;
        inner.SharedComponentVersionHash = _versionHash;
        SharedComponentTarget target = Resolve(workflow, "Sections[0]");

        ErrorOr<Success> result = _editor.ValidatePromotable(target);
        SharedComponentDefinition definition = _editor.BuildDefinition(target);

        Assert.False(result.IsError);
        JObject parsed = JObject.Parse(definition.DefinitionJson);
        var entries = (JArray)parsed[nameof(Section.Entries)]!;
        JToken refEntry = entries.Single(
            e => (string?)e[nameof(SectionEntry.Kind)] == nameof(SectionEntryKind.ComponentRef));
        Assert.Equal("s1b1", (string?)refEntry[nameof(SectionEntry.SlotTag)]);
        Assert.Equal("nested-component", (string?)refEntry[nameof(SectionEntry.SharedComponentId)]);
    }

    [Fact]
    public void ValidatePromotable_BlockInsideLinkedSection_Succeeds()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        first.SharedComponentId = "outer-component";
        first.SharedComponentLinkState = SharedComponentLinkState.Linked;

        ErrorOr<Success> result = _editor.ValidatePromotable(Resolve(workflow, "Sections[0].Blocks[0]"));

        Assert.False(result.IsError);
    }

    [Fact]
    public void ValidatePromotable_ExpandedSection_AndBlockInsideIt_Rejected()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        first.ExpandedFromParameterizedKey = "variant-a";

        ErrorOr<Success> sectionResult = _editor.ValidatePromotable(Resolve(workflow, "Sections[0]"));
        ErrorOr<Success> blockResult = _editor.ValidatePromotable(Resolve(workflow, "Sections[0].Blocks[0]"));

        Assert.True(sectionResult.IsError);
        Assert.True(blockResult.IsError);
    }

    [Fact]
    public void ValidatePromotable_SectionWithDuplicateInnerTags_Rejected()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        foreach (Block block in first.Blocks)
        {
            block.ReplacementTag = "same";
        }

        ErrorOr<Success> result = _editor.ValidatePromotable(Resolve(workflow, "Sections[0]"));

        Assert.True(result.IsError);
        Assert.Contains("same", result.FirstError.Description);
    }

    [Fact]
    public void ValidatePromotable_EmptySection_Rejected()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        first.Blocks.Clear();

        ErrorOr<Success> result = _editor.ValidatePromotable(Resolve(workflow, "Sections[0]"));

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, result.FirstError.Description);
    }

    [Fact]
    public void BuildDefinition_MatchesNormalizerOutputs_AndDefaultsName()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        Block block = first.Blocks.First();
        block.Name = "Counterparty check";
        SharedComponentTarget target = Resolve(workflow, "Sections[0].Blocks[0]");

        SharedComponentDefinition definition = _editor.BuildDefinition(target);

        Assert.Equal(SharedComponentKind.Block, definition.Kind);
        Assert.Equal(_normalizer.ToDefinitionJson(block), definition.DefinitionJson);
        Assert.Equal(_normalizer.ComputeContentHash(block), definition.ContentHash);
        Assert.Equal("Counterparty check", definition.DefaultName);
    }

    [Fact]
    public void StampLink_SetsQuartet_WithNullBindings()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        SharedComponentTarget target = Resolve(workflow, "Sections[0].Blocks[0]");

        _editor.StampLink(target, COMPONENT_ID, _versionHash);

        Block block = first.Blocks.First();
        Assert.Equal(COMPONENT_ID, block.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, block.SharedComponentLinkState);
        Assert.Equal(_versionHash, block.SharedComponentVersionHash);
        Assert.Null(block.SharedComponentBindings);
    }

    [Fact]
    public void MaterializeBlock_Appends_WithSlugTag_FreshStableId_AndQuartet()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        string definition = _normalizer.ToDefinitionJson(BuildDetachedBlock("Source prompt"));

        ErrorOr<MaterializedNode> result = _editor.Materialize(
            workflow, SharedComponentKind.Block, COMPONENT_ID, "FX Rate!", _versionHash,
            definition, first, index: null, requestedTag: null);

        Assert.False(result.IsError);
        Assert.Equal("fx_rate", result.Value.ReplacementTag);
        Assert.Equal("Sections[0].Blocks[2]", result.Value.CanonicalPath);
        Block landed = first.Blocks.Single(b => b.ReplacementTag == "fx_rate");
        Assert.False(string.IsNullOrEmpty(landed.StableBlockId));
        Assert.Equal(COMPONENT_ID, landed.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, landed.SharedComponentLinkState);
        Assert.Equal(_versionHash, landed.SharedComponentVersionHash);
        Assert.Null(landed.SharedComponentBindings);
        Assert.Equal([0, 1, 2], first.Blocks.OrderBy(b => b.Order).Select(b => b.Order));
    }

    [Fact]
    public void MaterializeBlock_RequestedTagCollision_Rejected()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        string definition = _normalizer.ToDefinitionJson(BuildDetachedBlock("Source prompt"));

        ErrorOr<MaterializedNode> result = _editor.Materialize(
            workflow, SharedComponentKind.Block, COMPONENT_ID, "FX", _versionHash,
            definition, first, index: null, requestedTag: "s1b1");

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Fact]
    public void MaterializeBlock_IntoSeparatorOrExpandedSection_Rejected()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out Section second);
        first.Properties.IsSeparator = true;
        first.Blocks.Clear();
        second.ExpandedFromParameterizedKey = "variant-a";
        string definition = _normalizer.ToDefinitionJson(BuildDetachedBlock("Source prompt"));

        ErrorOr<MaterializedNode> separator = _editor.Materialize(
            workflow, SharedComponentKind.Block, COMPONENT_ID, "FX", _versionHash,
            definition, first, null, null);
        ErrorOr<MaterializedNode> expanded = _editor.Materialize(
            workflow, SharedComponentKind.Block, COMPONENT_ID, "FX", _versionHash,
            definition, second, null, null);

        Assert.True(separator.IsError);
        Assert.True(expanded.IsError);
    }

    [Fact]
    public void MaterializeBlock_IntoLinkedSection_LandsAsLocalInsert()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);
        first.SharedComponentId = "outer-component";
        first.SharedComponentLinkState = SharedComponentLinkState.Linked;
        first.SharedComponentVersionHash = _versionHash;
        int blocksBefore = first.Blocks.Count;
        string definition = _normalizer.ToDefinitionJson(BuildDetachedBlock("Source prompt"));

        ErrorOr<MaterializedNode> result = _editor.Materialize(
            workflow, SharedComponentKind.Block, COMPONENT_ID, "FX", _versionHash,
            definition, first, null, null);

        Assert.False(result.IsError);
        Block landed = first.Blocks.Single(b => b.ReplacementTag == result.Value.ReplacementTag);
        Assert.Equal(COMPONENT_ID, landed.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, landed.SharedComponentLinkState);
        Assert.Equal(blocksBefore + 1, first.Blocks.Count);
        Assert.Equal("outer-component", first.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, first.SharedComponentLinkState);
    }

    [Fact]
    public void MaterializeSection_AtIndex_RenumbersAndStamps()
    {
        Workflow workflow = TwoSectionWorkflow(out _, out _);
        string definition = _normalizer.ToDefinitionJson(BuildDetachedSection("Risk", "risk_intro"));

        ErrorOr<MaterializedNode> result = _editor.Materialize(
            workflow, SharedComponentKind.Section, COMPONENT_ID, "Risk", _versionHash,
            definition, targetSection: null, index: 1, requestedTag: null);

        Assert.False(result.IsError);
        Assert.Equal("Sections[1]", result.Value.CanonicalPath);
        Assert.Null(result.Value.Bindings);
        Section landed = workflow.Sections.OrderBy(s => s.Order).ElementAt(1);
        Assert.Equal("Risk", landed.Title);
        Assert.Equal(COMPONENT_ID, landed.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, landed.SharedComponentLinkState);
        KeyValuePair<string, string> totalBinding = Assert.Single(landed.SharedComponentBindings!);
        Assert.Equal("risk_intro", totalBinding.Key);
        Assert.Equal("risk_intro", totalBinding.Value);
        Assert.False(string.IsNullOrEmpty(landed.StableSectionId));
        Assert.Equal([0, 1, 2], workflow.Sections.OrderBy(s => s.Order).Select(s => s.Order));
    }

    [Fact]
    public void MaterializeSection_CollidingInnerTags_RenamedRewrittenAndRecorded()
    {
        Workflow workflow = TwoSectionWorkflow(out _, out _);
        Section source = BuildDetachedSection("Risk", "s1b1");
        _ = new Block(BlockTypes.AiQuery, source, "reader")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Read {s1b1} and {s1b1.answer}" },
        };
        string definition = _normalizer.ToDefinitionJson(source);

        ErrorOr<MaterializedNode> result = _editor.Materialize(
            workflow, SharedComponentKind.Section, COMPONENT_ID, "Risk", _versionHash,
            definition, null, null, null);

        Assert.False(result.IsError);
        Assert.NotNull(result.Value.Bindings);
        string renamed = Assert.Single(result.Value.Bindings!.Keys.Where(k => k == "s1b1").Select(k => result.Value.Bindings![k]));
        Assert.NotEqual("s1b1", renamed);
        Section landed = workflow.Sections.OrderBy(s => s.Order).Last();
        Assert.Contains(landed.Blocks, b => b.ReplacementTag == renamed);
        Block reader = landed.Blocks.Single(b => b.ReplacementTag == "reader");
        string prompt = ((AiQueryProperties)reader.Properties!).UserPromptTemplate!;
        Assert.Contains($"{{{renamed}}}", prompt);
        Assert.Contains($"{{{renamed}.answer}}", prompt);
        Assert.DoesNotContain(result.Value.Bindings!, pair => pair.Key == "reader");
    }

    [Fact]
    public void MaterializeSection_RenameTargetAvoidsTheLandedSectionsOwnInnerTags()
    {
        Workflow workflow = TwoSectionWorkflow(out _, out _);
        Section source = BuildDetachedSection("Risk", "s1b1");
        _ = new Block(BlockTypes.AiQuery, source, "s1b1_2")
        {
            Name = "Block s1b1_2",
            Properties = new AiQueryProperties { UserPromptTemplate = "Prompt s1b1_2" },
        };
        string definition = _normalizer.ToDefinitionJson(source);

        ErrorOr<MaterializedNode> result = _editor.Materialize(
            workflow, SharedComponentKind.Section, COMPONENT_ID, "Risk", _versionHash,
            definition, null, null, null);

        Assert.False(result.IsError);
        Section landed = workflow.Sections.OrderBy(s => s.Order).Last();
        List<string> landedTags = [.. landed.Blocks.Select(b => b.ReplacementTag)];
        Assert.Equal(landedTags.Count, landedTags.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("s1b1_3", result.Value.Bindings!["s1b1"]);
    }

    [Fact]
    public void Materialize_CorruptPayload_ReturnsCorruptDefinition()
    {
        Workflow workflow = TwoSectionWorkflow(out Section first, out _);

        ErrorOr<MaterializedNode> result = _editor.Materialize(
            workflow, SharedComponentKind.Block, COMPONENT_ID, "FX", _versionHash,
            "not-json", first, null, null);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.CORRUPT_DEFINITION, result.FirstError.Code);
    }

    private SharedComponentTarget Resolve(Workflow workflow, string path)
    {
        ErrorOr<SharedComponentTarget> result = _editor.ResolveTarget(workflow, path, null);
        Assert.False(result.IsError);

        return result.Value;
    }

    private static Workflow TwoSectionWorkflow(out Section first, out Section second)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        first = new Section("S1", workflow) { Title = "S1" };
        AddBlock(first, "s1b1");
        AddBlock(first, "s1b2");
        second = new Section("S2", workflow) { Title = "S2" };
        AddBlock(second, "s2b1");

        return workflow;
    }

    private static void AddBlock(Section section, string tag)
    {
        _ = new Block(BlockTypes.AiQuery, section, tag)
        {
            Name = $"Block {tag}",
            Properties = new AiQueryProperties { UserPromptTemplate = $"Prompt {tag}" },
        };
    }

    private static Block BuildDetachedBlock(string prompt)
    {
        var scratch = new Workflow("scratch");
        var section = new Section("scratch", scratch);
        return new Block(BlockTypes.AiQuery, section, "source_tag")
        {
            Name = "Source block",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    private static Section BuildDetachedSection(string title, string innerTag)
    {
        var scratch = new Workflow("scratch");
        var section = new Section(title, scratch) { Title = title };
        _ = new Block(BlockTypes.AiQuery, section, innerTag)
        {
            Name = $"Block {innerTag}",
            Properties = new AiQueryProperties { UserPromptTemplate = $"Prompt {innerTag}" },
        };

        return section;
    }
}
