using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class SharedComponentReconcilerTests
{
    private const string COMPONENT_ID = "component-1";
    private static readonly string _headHash = new('a', 64);
    private static readonly string _laggingHash = new('b', 64);
    private static readonly string _editedHash = new('c', 64);

    private readonly WorkflowCanonicalizer _canonicalizer = new();
    private readonly SharedComponentContentNormalizer _normalizer;
    private readonly SharedComponentReconciler _reconciler;

    public SharedComponentReconcilerTests()
    {
        _normalizer = new SharedComponentContentNormalizer(_canonicalizer);
        _reconciler = new SharedComponentReconciler(_normalizer);
    }

    [Fact]
    public void Classify_InstanceAndLinkAtHead_InSync()
    {
        SharedComponentRelationResult result = _reconciler.Classify(
            ActiveComponent(), _headHash, _headHash, _headHash);

        Assert.Equal(SharedComponentRelation.InSync, result.Relation);
        Assert.False(result.LinkHashStale);
    }

    [Fact]
    public void Classify_InstanceAtHeadWithLaggingLinkStamp_InSyncAndLinkHashStale()
    {
        SharedComponentRelationResult result = _reconciler.Classify(
            ActiveComponent(), _headHash, _headHash, _laggingHash);

        Assert.Equal(SharedComponentRelation.InSync, result.Relation);
        Assert.True(result.LinkHashStale);
    }

    [Fact]
    public void Classify_UntouchedInstanceBehindHead_HeadAhead()
    {
        SharedComponentRelationResult result = _reconciler.Classify(
            ActiveComponent(), _headHash, _laggingHash, _laggingHash);

        Assert.Equal(SharedComponentRelation.HeadAhead, result.Relation);
        Assert.False(result.LinkHashStale);
    }

    [Fact]
    public void Classify_EditedInstanceOnCurrentLink_LocalAhead()
    {
        SharedComponentRelationResult result = _reconciler.Classify(
            ActiveComponent(), _headHash, _editedHash, _headHash);

        Assert.Equal(SharedComponentRelation.LocalAhead, result.Relation);
        Assert.False(result.LinkHashStale);
    }

    [Fact]
    public void Classify_AllThreeHashesDiffer_Diverged()
    {
        SharedComponentRelationResult result = _reconciler.Classify(
            ActiveComponent(), _headHash, _editedHash, _laggingHash);

        Assert.Equal(SharedComponentRelation.Diverged, result.Relation);
        Assert.False(result.LinkHashStale);
    }

    [Fact]
    public void Classify_MissingLinkStamp_ComputableFromInstanceAndHead()
    {
        SharedComponentRelationResult offHead = _reconciler.Classify(
            ActiveComponent(), _headHash, _editedHash, null);
        SharedComponentRelationResult atHead = _reconciler.Classify(
            ActiveComponent(), _headHash, _headHash, null);

        Assert.Equal(SharedComponentRelation.Diverged, offHead.Relation);
        Assert.Equal(SharedComponentRelation.InSync, atHead.Relation);
        Assert.True(atHead.LinkHashStale);
    }

    [Fact]
    public void Classify_RetiredComponent_ShortCircuitsBeforeHashCompare()
    {
        var component = ActiveComponent();
        component.State = SharedComponentState.Retired;

        SharedComponentRelationResult result = _reconciler.Classify(
            component, _headHash, _headHash, _headHash);

        Assert.Equal(SharedComponentRelation.ComponentRetired, result.Relation);
        Assert.False(result.LinkHashStale);
    }

    [Fact]
    public void Classify_UnknownComponent_ComponentMissing()
    {
        SharedComponentRelationResult result = _reconciler.Classify(
            component: null, _headHash, _headHash, _headHash);

        Assert.Equal(SharedComponentRelation.ComponentMissing, result.Relation);
    }

    [Fact]
    public void Classify_VersionlessChain_ComponentMissing()
    {
        SharedComponentRelationResult result = _reconciler.Classify(
            ActiveComponent(), headContentHash: null, _headHash, _headHash);

        Assert.Equal(SharedComponentRelation.ComponentMissing, result.Relation);
    }

    [Fact]
    public void ResolveDefinitionVersion_NewestPayloadPresentMatchWins()
    {
        SharedComponentVersion[] versions =
        [
            Version(1, _headHash),
            Version(2, _headHash),
            Version(3, _laggingHash),
        ];

        SharedComponentVersion? resolved = _reconciler.ResolveDefinitionVersion(versions, _headHash);

        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.SequenceNumber);
    }

    [Fact]
    public void ResolveDefinitionVersion_SkipsPayloadAbsentRows()
    {
        SharedComponentVersion[] versions =
        [
            Version(1, _headHash),
            Version(2, _headHash, payloadAbsent: true),
        ];

        SharedComponentVersion? resolved = _reconciler.ResolveDefinitionVersion(versions, _headHash);

        Assert.NotNull(resolved);
        Assert.Equal(1, resolved.SequenceNumber);
    }

    [Fact]
    public void ResolveDefinitionVersion_ForeignOrAbsentHash_ReturnsNull()
    {
        SharedComponentVersion[] versions = [Version(1, _headHash)];

        Assert.Null(_reconciler.ResolveDefinitionVersion(versions, _editedHash));
        Assert.Null(_reconciler.ResolveDefinitionVersion(versions, null));
    }

    [Fact]
    public void MaterializedSection_WithCollisionBindings_ReverseHashesToDefinition_InSync()
    {
        var editor = new SharedComponentEditor(_canonicalizer, _normalizer);
        Workflow workflow = WorkflowWithTag("s1b1");
        Section source = DetachedSection("Risk", "s1b1");
        _ = new Block(BlockTypes.AiQuery, source, "reader")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Read {s1b1} and {s1b1.answer}" },
        };
        string definitionJson = _normalizer.ToDefinitionJson(source);
        string definitionHash = _normalizer.ComputeContentHash(source);

        ErrorOr<MaterializedNode> landed = editor.Materialize(
            workflow, SharedComponentKind.Section, COMPONENT_ID, "Risk", definitionHash,
            definitionJson, null, null, null);

        Assert.False(landed.IsError);
        Section instance = workflow.Sections.OrderBy(s => s.Order).Last();
        Assert.NotNull(instance.SharedComponentBindings);
        Assert.NotEqual(definitionHash, _normalizer.ComputeContentHash(instance));

        string instanceHash = _reconciler.ComputeInstanceContentHash(instance);
        Assert.Equal(definitionHash, instanceHash);

        SharedComponentRelationResult relation = _reconciler.Classify(
            ActiveComponent(), definitionHash, instanceHash, instance.SharedComponentVersionHash);
        Assert.Equal(SharedComponentRelation.InSync, relation.Relation);
        Assert.False(relation.LinkHashStale);
    }

    [Fact]
    public void MaterializedBlock_RootTagRewritten_StillHashesToDefinition()
    {
        var editor = new SharedComponentEditor(_canonicalizer, _normalizer);
        Workflow workflow = WorkflowWithTag("existing");
        Section target = workflow.Sections.Single();
        var scratchWorkflow = new Workflow("scratch");
        var scratchSection = new Section("scratch", scratchWorkflow);
        var sourceBlock = new Block(BlockTypes.AiQuery, scratchSection, "source_tag")
        {
            Name = "Source block",
            Properties = new AiQueryProperties { UserPromptTemplate = "Shared prompt" },
        };
        string definitionJson = _normalizer.ToDefinitionJson(sourceBlock);
        string definitionHash = _normalizer.ComputeContentHash(sourceBlock);

        ErrorOr<MaterializedNode> landed = editor.Materialize(
            workflow, SharedComponentKind.Block, COMPONENT_ID, "Source block", definitionHash,
            definitionJson, target, null, null);

        Assert.False(landed.IsError);
        Block instance = target.Blocks.Single(b => b.ReplacementTag == landed.Value.ReplacementTag);
        Assert.Null(instance.SharedComponentBindings);
        Assert.Equal(definitionHash, _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void ApplySection_UntouchedConsumer_GraftsMatchedSlots_InSync()
    {
        Workflow workflow = WorkflowWithTag("metric");
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("metric", "Compute risk"), ("reader", "Read {metric} and {metric.answer}"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        List<string?> stableIds = [.. instance.Blocks.OrderBy(b => b.Order).Select(b => b.StableBlockId)];

        Section v2 = DetachedSectionWithBlocks(
            "Risk v2", ("metric", "Compute risk carefully"), ("reader", "Read {metric} and {metric.answer} twice"));
        v2.Blocks.OrderBy(b => b.Order).First().SupportedDocumentClasses.Add("Contract");

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.RemovedBlocks);
        Assert.Empty(result.AddedBlocks);
        Assert.True(result.OldDefinitionAvailable);
        List<Block> ordered = [.. instance.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(stableIds, ordered.Select(b => b.StableBlockId));
        Assert.Equal(["metric_2", "reader"], ordered.Select(b => b.ReplacementTag));
        Assert.Equal("Risk", instance.Title);
        Assert.Equal(["Contract"], ordered[0].SupportedDocumentClasses);
        Assert.Equal(
            "Read {metric_2} and {metric_2.answer} twice",
            ((AiQueryProperties)ordered[1].Properties).UserPromptTemplate);
        Assert.Equal("metric_2", instance.SharedComponentBindings!["metric"]);
        Assert.Equal(_normalizer.ComputeContentHash(v2), _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void ApplySection_InlineSlot_ClearsStrayLinkOnLiveBlock()
    {
        Workflow workflow = WorkflowWithTag("metric");
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("metric", "Compute risk"), ("reader", "Read the metric"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Block live = instance.Blocks.OrderBy(b => b.Order).First();
        live.SharedComponentId = "stray-component";
        live.SharedComponentLinkState = SharedComponentLinkState.Linked;
        live.SharedComponentVersionHash = new string('d', 64);
        live.SharedComponentBindings = new Dictionary<string, string> { ["slot"] = "tag" };

        Section v2 = DetachedSectionWithBlocks(
            "Risk v2", ("metric", "Compute risk carefully"), ("reader", "Read the metric"));

        _ = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Null(live.SharedComponentId);
        Assert.Null(live.SharedComponentLinkState);
        Assert.Null(live.SharedComponentVersionHash);
        Assert.Null(live.SharedComponentBindings);
        Assert.Equal("Compute risk carefully", ((AiQueryProperties)live.Properties).UserPromptTemplate);
    }

    [Fact]
    public void ApplySection_AddedSlot_FreeTagKept_NoBinding_InSync()
    {
        Workflow workflow = WorkflowWithTag("metric");
        Section v1 = DetachedSectionWithBlocks("Risk", ("metric", "Compute risk"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);

        Section v2 = DetachedSectionWithBlocks(
            "Risk", ("metric", "Compute risk"), ("gamma", "Fresh view of {metric}"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Block added = Assert.Single(result.AddedBlocks);
        Assert.Equal("gamma", added.ReplacementTag);
        Assert.NotNull(added.StableBlockId);
        Assert.Equal(
            "Fresh view of {metric_2}",
            ((AiQueryProperties)added.Properties).UserPromptTemplate);
        Assert.Equal("metric_2", instance.SharedComponentBindings!["metric"]);
        Assert.Equal("gamma", instance.SharedComponentBindings["gamma"]);
        Assert.Equal(2, instance.SharedComponentBindings.Count);
        Assert.Equal(_normalizer.ComputeContentHash(v2), _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void ApplySection_AddedSlotCollidingWithInstanceTag_SlugAndChainSafeRewrites()
    {
        Workflow workflow = WorkflowWithTag("metric");
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("metric", "Compute risk"), ("reader", "Read {metric}"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);

        Section v2 = DetachedSectionWithBlocks(
            "Risk",
            ("metric", "Compute risk"),
            ("reader", "Read {metric} and {metric_2}"),
            ("metric_2", "Also {metric} and {metric_2}"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Block added = Assert.Single(result.AddedBlocks);
        Assert.Equal("metric_2_2", added.ReplacementTag);
        Assert.Equal("metric_2", instance.SharedComponentBindings!["metric"]);
        Assert.Equal("metric_2_2", instance.SharedComponentBindings["metric_2"]);
        Assert.Equal(
            "Also {metric_2} and {metric_2_2}",
            ((AiQueryProperties)added.Properties).UserPromptTemplate);
        Block reader = instance.Blocks.Single(b => b.ReplacementTag == "reader");
        Assert.Equal(
            "Read {metric_2} and {metric_2_2}",
            ((AiQueryProperties)reader.Properties).UserPromptTemplate);
        Assert.Equal(_normalizer.ComputeContentHash(v2), _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void ApplySection_RemovedSlot_DroppedAndReported_BindingCleared()
    {
        Workflow workflow = WorkflowWithTag("metric");
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("metric", "Compute risk"), ("reader", "Read carefully"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        string? removedStableId = instance.Blocks.Single(b => b.ReplacementTag == "metric_2").StableBlockId;

        Section v2 = DetachedSectionWithBlocks("Risk", ("reader", "Read carefully"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Block removed = Assert.Single(result.RemovedBlocks);
        Assert.Equal("metric_2", removed.ReplacementTag);
        Assert.Equal(removedStableId, removed.StableBlockId);
        Assert.Equal("reader", Assert.Single(instance.Blocks).ReplacementTag);
        KeyValuePair<string, string> onlyBinding = Assert.Single(instance.SharedComponentBindings!);
        Assert.Equal("reader", onlyBinding.Key);
        Assert.Equal("reader", onlyBinding.Value);
        Assert.Equal(_normalizer.ComputeContentHash(v2), _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void ApplySection_PureSlotRename_RescuedByContentHash_KeepsStableIdAndInstanceTag()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt"), ("reader", "See {alpha}"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        string? alphaStableId = instance.Blocks.Single(b => b.ReplacementTag == "alpha").StableBlockId;

        Section v2 = DetachedSectionWithBlocks(
            "Risk", ("beta", "Alpha prompt"), ("reader", "See {beta}"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.RemovedBlocks);
        Assert.Empty(result.AddedBlocks);
        Assert.Equal("beta", result.RenamedSlots["alpha"]);
        Block rescued = instance.Blocks.Single(b => b.ReplacementTag == "alpha");
        Assert.Equal(alphaStableId, rescued.StableBlockId);
        Assert.Equal("alpha", instance.SharedComponentBindings!["beta"]);
        Block reader = instance.Blocks.Single(b => b.ReplacementTag == "reader");
        Assert.Equal("See {alpha}", ((AiQueryProperties)reader.Properties).UserPromptTemplate);
        Assert.Equal(_normalizer.ComputeContentHash(v2), _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void ApplySection_LocalExtraBlock_SurvivesAfterItsMatchedNeighbor()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        var extra = new Block(BlockTypes.AiQuery, instance, 1, "custom")
        {
            Name = "Local extra",
            Properties = new AiQueryProperties { UserPromptTemplate = "Custom {alpha}" },
        };

        Section v2 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt v2"), ("beta", "Beta prompt v2"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.RemovedBlocks);
        Assert.Empty(result.AddedBlocks);
        List<Block> ordered = [.. instance.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["alpha", "custom", "beta"], ordered.Select(b => b.ReplacementTag));
        Assert.Same(extra, ordered[1]);
        Assert.Equal("Custom {alpha}", ((AiQueryProperties)extra.Properties).UserPromptTemplate);
        Assert.NotEqual(_normalizer.ComputeContentHash(v2), _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void ApplySection_LeadingLocalExtraBlock_KeepsTheFrontOfTheSection()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        var extra = new Block(BlockTypes.AiQuery, instance, 0, "custom")
        {
            Name = "Local extra",
            Properties = new AiQueryProperties { UserPromptTemplate = "Custom intro" },
        };

        Section v2 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt v2"), ("beta", "Beta prompt v2"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.RemovedBlocks);
        Assert.Empty(result.AddedBlocks);
        List<Block> ordered = [.. instance.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["custom", "alpha", "beta"], ordered.Select(b => b.ReplacementTag));
        Assert.Same(extra, ordered[0]);
    }

    [Fact]
    public void ApplySection_TwoLeadingLocalExtras_PreserveTheirRelativeOrder()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        _ = new Block(BlockTypes.AiQuery, instance, 0, "custom_first")
        {
            Name = "Local extra",
            Properties = new AiQueryProperties { UserPromptTemplate = "First intro" },
        };
        _ = new Block(BlockTypes.AiQuery, instance, 1, "custom_second")
        {
            Name = "Local extra",
            Properties = new AiQueryProperties { UserPromptTemplate = "Second intro" },
        };

        Section v2 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt v2"), ("beta", "Beta prompt v2"));

        _ = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        List<Block> ordered = [.. instance.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["custom_first", "custom_second", "alpha", "beta"], ordered.Select(b => b.ReplacementTag));
    }

    [Fact]
    public void ApplySection_AddedSlotCollidingWithLocalExtraTag_SluggedToKeepTagsUnique()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        var extra = new Block(BlockTypes.AiQuery, instance, 1, "custom")
        {
            Name = "Local extra",
            Properties = new AiQueryProperties { UserPromptTemplate = "Custom {alpha}" },
        };

        Section v2 = DetachedSectionWithBlocks(
            "Risk",
            ("alpha", "Alpha prompt"),
            ("beta", "Beta prompt"),
            ("custom", "Slot view of {custom}"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Block added = Assert.Single(result.AddedBlocks);
        Assert.Equal("custom_2", added.ReplacementTag);
        Assert.Equal("custom_2", instance.SharedComponentBindings!["custom"]);
        Assert.Equal(
            "Slot view of {custom_2}",
            ((AiQueryProperties)added.Properties).UserPromptTemplate);
        List<Block> ordered = [.. instance.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["alpha", "custom", "beta", "custom_2"], ordered.Select(b => b.ReplacementTag));
        Assert.Same(extra, ordered[1]);
        Assert.Equal("Custom {alpha}", ((AiQueryProperties)extra.Properties).UserPromptTemplate);
    }

    [Fact]
    public void ApplySection_MatchedSlotLocallyDeleted_ReaddedWithFreshStableId()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Block deleted = instance.Blocks.Single(b => b.ReplacementTag == "beta");
        string? deletedStableId = deleted.StableBlockId;
        instance.Remove(deleted);

        Section v2 = DetachedSectionWithBlocks(
            "Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Block readded = Assert.Single(result.AddedBlocks);
        Assert.Equal("beta", readded.ReplacementTag);
        Assert.NotNull(readded.StableBlockId);
        Assert.NotEqual(deletedStableId, readded.StableBlockId);
        Assert.Equal(_normalizer.ComputeContentHash(v2), _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void ApplySection_ForeignOldDefinition_NothingRemoved_MatchedByBindings()
    {
        Workflow workflow = WorkflowWithTag("metric");
        Section v1 = DetachedSectionWithBlocks(
            "Risk", ("metric", "Compute risk"), ("reader", "Read {metric}"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        List<string?> stableIds = [.. instance.Blocks.OrderBy(b => b.Order).Select(b => b.StableBlockId)];

        Section v2 = DetachedSectionWithBlocks(
            "Risk", ("metric", "Compute risk v2"), ("reader", "Read {metric} v2"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, oldDefinition: null, ParseDefinition(v2));

        Assert.False(result.OldDefinitionAvailable);
        Assert.Empty(result.RemovedBlocks);
        Assert.Empty(result.AddedBlocks);
        Assert.Equal(stableIds, instance.Blocks.OrderBy(b => b.Order).Select(b => b.StableBlockId));
        Assert.Equal(_normalizer.ComputeContentHash(v2), _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void ApplySection_HiddenSlot_NotReadded_TombstonePreserved()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        instance.Remove(instance.Blocks.Single(b => b.ReplacementTag == "beta"));
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["beta"] = SharedComponentSlotState.Hidden,
        };

        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt v2"), ("beta", "Beta prompt v2"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.AddedBlocks);
        Block remaining = Assert.Single(instance.Blocks);
        Assert.Equal("alpha", remaining.ReplacementTag);
        Assert.Equal("Alpha prompt v2", ((AiQueryProperties)remaining.Properties).UserPromptTemplate);
        Assert.Equal(SharedComponentSlotState.Hidden, instance.SharedComponentSlotStates!["beta"]);
    }

    [Fact]
    public void ApplySection_BrokenOutSlot_LocalContentUntouched()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Block broken = instance.Blocks.Single(b => b.ReplacementTag == "alpha");
        ((AiQueryProperties)broken.Properties).UserPromptTemplate = "My local fork";
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["alpha"] = SharedComponentSlotState.BrokenOut,
        };

        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt v2"), ("beta", "Beta prompt v2"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.AddedBlocks);
        Assert.Empty(result.RemovedBlocks);
        Assert.Equal(
            "My local fork",
            ((AiQueryProperties)instance.Blocks.Single(b => b.ReplacementTag == "alpha").Properties).UserPromptTemplate);
        Assert.Equal(
            "Beta prompt v2",
            ((AiQueryProperties)instance.Blocks.Single(b => b.ReplacementTag == "beta").Properties).UserPromptTemplate);
        Assert.Equal("alpha", instance.SharedComponentBindings!["alpha"]);
    }

    [Fact]
    public void ApplySection_RenamedSlot_SlotStateKeyFollowsTheRename()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("reader", "See {alpha}"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["alpha"] = SharedComponentSlotState.BrokenOut,
        };

        Section v2 = DetachedSectionWithBlocks("Risk", ("beta", "Alpha prompt"), ("reader", "See {beta}"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Equal("beta", result.RenamedSlots["alpha"]);
        Assert.Equal(
            SharedComponentSlotState.BrokenOut,
            Assert.Single(instance.SharedComponentSlotStates!).Value);
        Assert.Equal("beta", Assert.Single(instance.SharedComponentSlotStates!).Key);
    }

    [Fact]
    public void ApplySection_RefEntryInDefinition_NotHydratedInWaveOne()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);

        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt v2"));
        v2.Entries =
        [
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
            },
        ];

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.AddedBlocks);
        Block only = Assert.Single(instance.Blocks);
        Assert.Equal("alpha", only.ReplacementTag);
        Assert.Equal("Alpha prompt v2", ((AiQueryProperties)only.Properties).UserPromptTemplate);
    }

    [Fact]
    public void ComputeSlotDelta_RefEntryRenamed_RescuedByTargetAndBindings()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("intro", "Intro prompt"));
        oldDef.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
            },
        ];
        Section newDef = DetachedSectionWithBlocks("Risk", ("intro", "Intro prompt"));
        newDef.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc_v2",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
            },
        ];

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        Assert.Equal(["intro"], delta.MatchedSlotTags);
        Assert.Equal("kyc_v2", delta.RenamedSlots["kyc"]);
        Assert.Empty(delta.RemovedSlotTags);
        Assert.Empty(delta.AddedSlotTags);
    }

    [Fact]
    public void ComputeSlotDelta_RefEntryRetargeted_NoRescue()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("intro", "Intro prompt"));
        oldDef.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
            },
        ];
        Section newDef = DetachedSectionWithBlocks("Risk", ("intro", "Intro prompt"));
        newDef.Entries =
        [
            new SectionEntry { SlotTag = "risk", Kind = SectionEntryKind.ComponentRef, SharedComponentId = "component-risk" },
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
        ];

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        Assert.Equal(["kyc"], delta.RemovedSlotTags);
        Assert.Equal(["risk"], delta.AddedSlotTags);
        Assert.Empty(delta.RenamedSlots);
    }

    [Fact]
    public void ComputeSlotDelta_SlotConvertedInlineToRef_MatchedNotRemoved()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("intro", "Intro prompt"), ("kyc", "KYC prompt"));
        Section newDef = DetachedSectionWithBlocks("Risk", ("intro", "Intro prompt"));
        newDef.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
            },
        ];

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        Assert.Equal(["intro", "kyc"], delta.MatchedSlotTags);
        Assert.Empty(delta.RemovedSlotTags);
        Assert.Empty(delta.AddedSlotTags);
        Assert.Empty(delta.RenamedSlots);
        Assert.Empty(delta.RescuedSlots);
    }

    [Fact]
    public void ComputeInertPlaceholders_SlotConvertedInlineToRef_ReportsNothing()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("kyc", "Who signs for {client}?"));
        ((AiQueryProperties)oldDef.Blocks.Single().Properties).SignatureFields =
            new Dictionary<string, SignatureFieldDefinition>
            {
                ["client"] = new("Client", "Signing client", "the client"),
            };
        Section newDef = DetachedSectionWithBlocks("Risk");
        newDef.Entries =
        [
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
            },
        ];

        IReadOnlyDictionary<string, IReadOnlyList<string>> inert =
            SharedComponentReconciler.ComputeInertPlaceholders(oldDef, newDef, ["kyc"]);

        Assert.Empty(inert);
    }

    [Fact]
    public void ComputeSlotDelta_NameMatch_RescuesAcrossTagChange()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        oldDef.Blocks.Single().Name = "Risk Analysis";
        Section newDef = DetachedSectionWithBlocks("Risk", ("beta", "Beta prompt"));
        newDef.Blocks.Single().Name = " risk analysis ";

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        SharedComponentSlotRescue rescue = Assert.Single(delta.RescuedSlots);
        Assert.Equal("alpha", rescue.OldTag);
        Assert.Equal("beta", rescue.NewTag);
        Assert.Equal(SharedComponentSlotRescueReason.NameMatch, rescue.Reason);
        Assert.Empty(delta.RemovedSlotTags);
        Assert.Empty(delta.AddedSlotTags);
        Assert.Empty(delta.RenamedSlots);
    }

    [Fact]
    public void ComputeSlotDelta_SamePositionSimilarTag_RescuesWithoutNameMatch()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("metric_intro", "First prompt"));
        oldDef.Blocks.Single().Name = "First Block";
        Section newDef = DetachedSectionWithBlocks("Risk", ("metric_intro2", "Second prompt"));
        newDef.Blocks.Single().Name = "Second Block";

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        SharedComponentSlotRescue rescue = Assert.Single(delta.RescuedSlots);
        Assert.Equal("metric_intro", rescue.OldTag);
        Assert.Equal("metric_intro2", rescue.NewTag);
        Assert.Equal(SharedComponentSlotRescueReason.PositionAndTagSimilarity, rescue.Reason);
        Assert.Empty(delta.RemovedSlotTags);
        Assert.Empty(delta.AddedSlotTags);
    }

    [Fact]
    public void ComputeSlotDelta_AmbiguousNameMatch_NoRescue()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("a1", "P1"), ("a2", "P2"));
        Section newDef = DetachedSectionWithBlocks("Risk", ("b1", "P3"), ("b2", "P4"));
        foreach (Block block in oldDef.Blocks.Concat(newDef.Blocks))
        {
            block.Name = "Dup";
        }

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        Assert.Empty(delta.RescuedSlots);
        Assert.Equal(["a1", "a2"], delta.RemovedSlotTags);
        Assert.Equal(["b1", "b2"], delta.AddedSlotTags);
    }

    [Fact]
    public void ComputeSlotDelta_BlockTypeMismatch_NoRescue()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        oldDef.Blocks.Single().Name = "Same Name";
        var scratch = new Workflow("scratch");
        var newDef = new Section("Risk", scratch) { Title = "Risk" };
        _ = new Block(BlockTypes.Merge, newDef, "zzzzz")
        {
            Name = "Same Name",
            Properties = new MergeProperties(),
        };

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        Assert.Empty(delta.RescuedSlots);
        Assert.Equal(["alpha"], delta.RemovedSlotTags);
        Assert.Equal(["zzzzz"], delta.AddedSlotTags);
    }

    [Fact]
    public void ComputeSlotDelta_TagSimilarityAtBoundary_Rescues()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("abcde", "First prompt"));
        oldDef.Blocks.Single().Name = "First Block";
        Section newDef = DetachedSectionWithBlocks("Risk", ("abxye", "Second prompt"));
        newDef.Blocks.Single().Name = "Second Block";

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        SharedComponentSlotRescue rescue = Assert.Single(delta.RescuedSlots);
        Assert.Equal(SharedComponentSlotRescueReason.PositionAndTagSimilarity, rescue.Reason);
    }

    [Fact]
    public void ComputeSlotDelta_TagSimilarityBelowBoundary_NoRescue()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("abcde", "First prompt"));
        oldDef.Blocks.Single().Name = "First Block";
        Section newDef = DetachedSectionWithBlocks("Risk", ("axyze", "Second prompt"));
        newDef.Blocks.Single().Name = "Second Block";

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        Assert.Empty(delta.RescuedSlots);
        Assert.Equal(["abcde"], delta.RemovedSlotTags);
        Assert.Equal(["axyze"], delta.AddedSlotTags);
    }

    [Fact]
    public void ComputeSlotDelta_NameMatchConsumesBeforePositionTier()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("data_alpha", "P1"));
        oldDef.Blocks.Single().Name = "Metrics";
        Section newDef = DetachedSectionWithBlocks("Risk", ("data_alph", "P2"), ("zz_slot", "P3"));
        newDef.Blocks.Single(b => b.ReplacementTag == "data_alph").Name = "Other";
        newDef.Blocks.Single(b => b.ReplacementTag == "zz_slot").Name = "Metrics";

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);

        SharedComponentSlotRescue rescue = Assert.Single(delta.RescuedSlots);
        Assert.Equal("data_alpha", rescue.OldTag);
        Assert.Equal("zz_slot", rescue.NewTag);
        Assert.Equal(SharedComponentSlotRescueReason.NameMatch, rescue.Reason);
        Assert.Empty(delta.RemovedSlotTags);
        Assert.Equal(["data_alph"], delta.AddedSlotTags);
    }

    [Fact]
    public void EntryAvailableActions_OverridesAndAbsences_UseFixedArms()
    {
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.Unhide],
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.Hidden,
                relation: null, insertLinkState: null, deletedByDefinition: false, liveMissing: false));
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.AdoptHead],
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.BrokenOut,
                relation: null, insertLinkState: null, deletedByDefinition: false, liveMissing: false));
        Assert.Equal(
            [
                SharedComponentReconcileAction.Hide,
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.KeepLocal,
            ],
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
                relation: null, insertLinkState: null, deletedByDefinition: false, liveMissing: true));
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.UpdateShared],
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.DefinitionInline, SharedComponentEntryOverrideState.None,
                relation: null, insertLinkState: null, deletedByDefinition: true, liveMissing: false));
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.UpdateShared],
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.LocalInsert, SharedComponentEntryOverrideState.None,
                relation: null, insertLinkState: null, deletedByDefinition: false, liveMissing: false));
    }

    [Fact]
    public void EntryAvailableActions_LinkedInsert_StandardBlockArmsWithoutRelinkPlusUpdateShared()
    {
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.UpdateShared],
            SharedComponentReconciler.EntryAvailableActions(
                SharedComponentEntryOrigin.LocalInsert, SharedComponentEntryOverrideState.None,
                SharedComponentRelation.InSync, SharedComponentLinkState.BrokenOut,
                deletedByDefinition: false, liveMissing: false));
    }

    [Fact]
    public void EntryAvailableActions_ByRelation_DefaultFirst()
    {
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal],
            EntryArmsFor(SharedComponentRelation.InSync));
        Assert.Equal(
            [
                SharedComponentReconcileAction.KeepLocal,
                SharedComponentReconcileAction.UpdateShared,
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.BreakOut,
            ],
            EntryArmsFor(SharedComponentRelation.LocalAhead));
        Assert.Equal(
            [
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.BreakOut,
                SharedComponentReconcileAction.KeepLocal,
            ],
            EntryArmsFor(SharedComponentRelation.HeadAhead));
        Assert.Equal(
            [
                SharedComponentReconcileAction.UpdateShared,
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.BreakOut,
                SharedComponentReconcileAction.KeepLocal,
            ],
            EntryArmsFor(SharedComponentRelation.Diverged));
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.BreakOut],
            EntryArmsFor(SharedComponentRelation.ComponentRetired));
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.BreakOut],
            EntryArmsFor(SharedComponentRelation.ComponentMissing));
    }

    private static IReadOnlyList<SharedComponentReconcileAction> EntryArmsFor(SharedComponentRelation relation)
    {
        return SharedComponentReconciler.EntryAvailableActions(
            SharedComponentEntryOrigin.DefinitionRef, SharedComponentEntryOverrideState.None,
            relation, insertLinkState: null, deletedByDefinition: false, liveMissing: false);
    }

    [Fact]
    public void ComputeInertPlaceholders_RescuedPair_ReportsDroppedPlaceholders()
    {
        Section oldDef = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        Block oldBlock = oldDef.Blocks.Single();
        oldBlock.Name = "Signer";
        ((AiQueryProperties)oldBlock.Properties).SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["sig_a"] = new("A", "Signer A", "the first signer"),
            ["sig_b"] = new("B", "Signer B", "the second signer"),
        };
        Section newDef = DetachedSectionWithBlocks("Risk", ("beta", "Beta prompt"));
        Block newBlock = newDef.Blocks.Single();
        newBlock.Name = "Signer";
        ((AiQueryProperties)newBlock.Properties).SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["sig_a"] = new("A", "Signer A", "the first signer"),
        };

        SharedComponentSlotDelta delta = _reconciler.ComputeSlotDelta(oldDef, newDef);
        IReadOnlyDictionary<string, IReadOnlyList<string>> inert = SharedComponentReconciler.ComputeInertPlaceholders(
            oldDef, newDef, delta.MatchedSlotTags, delta.RescuedSlots);

        _ = Assert.Single(delta.RescuedSlots);
        (string slotTag, IReadOnlyList<string> dropped) = Assert.Single(inert);
        Assert.Equal("beta", slotTag);
        Assert.Equal(["sig_b"], dropped);
    }

    [Fact]
    public void ApplySectionDefinition_NameRescuedSlot_GraftsContentKeepsIdentity()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        v1.Blocks.Single().Name = "Risk Analysis";
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        string stableIdBefore = instance.Blocks.Single().StableBlockId!;

        Section v2 = DetachedSectionWithBlocks("Risk", ("beta", "Beta prompt"));
        v2.Blocks.Single().Name = "Risk Analysis";

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Block live = Assert.Single(instance.Blocks);
        Assert.Equal(stableIdBefore, live.StableBlockId);
        Assert.Equal("alpha", live.ReplacementTag);
        Assert.Equal("Beta prompt", ((AiQueryProperties)live.Properties).UserPromptTemplate);
        Assert.Equal("alpha", instance.SharedComponentBindings!["beta"]);
        Assert.Equal("beta", result.RenamedSlots["alpha"]);
        Assert.Empty(result.RemovedBlocks);
        Assert.Empty(result.AddedBlocks);
    }

    [Fact]
    public void BlockHash_BindingsAware_ReverseRenamesInputTagsAndReferences()
    {
        var scratchDef = new Workflow("scratch");
        var defSection = new Section("scratch", scratchDef);
        var definition = new Block(BlockTypes.AiQuery, defSection, "slot")
        {
            Name = "KYC Check",
            Properties = new AiQueryProperties { UserPromptTemplate = "Use {b_input} here" },
        };
        _ = new TextInput(definition, "b_input", "seed");

        var scratchInstance = new Workflow("consumer");
        var instanceSection = new Section("s", scratchInstance);
        var instance = new Block(BlockTypes.AiQuery, instanceSection, "kyc")
        {
            Name = "KYC Check",
            Properties = new AiQueryProperties { UserPromptTemplate = "Use {kyc_input} here" },
        };
        _ = new TextInput(instance, "kyc_input", "seed");
        instance.SharedComponentBindings = new Dictionary<string, string> { ["b_input"] = "kyc_input" };

        Assert.NotEqual(
            _normalizer.ComputeContentHash(definition),
            _normalizer.ComputeContentHash(instance));
        Assert.Equal(
            _normalizer.ComputeContentHash(definition),
            _reconciler.ComputeInstanceContentHash(instance));
    }

    [Fact]
    public void InstanceHash_HiddenSlot_HashCancels_FlagsOverrides()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        instance.Remove(instance.Blocks.Single(b => b.ReplacementTag == "beta"));
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["beta"] = SharedComponentSlotState.Hidden,
        };

        SectionInstanceHashResult result = _reconciler.ComputeInstanceContentHash(
            instance, () => Section.FromJson(v1Json));

        Assert.Equal(_normalizer.ComputeContentHash(v1), result.ContentHash);
        Assert.True(result.HasLocalOverrides);
    }

    [Fact]
    public void InstanceHash_BrokenOutSlot_LocalDriftCancels()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Block broken = instance.Blocks.Single(b => b.ReplacementTag == "alpha");
        ((AiQueryProperties)broken.Properties).UserPromptTemplate = "My local fork";
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["alpha"] = SharedComponentSlotState.BrokenOut,
        };

        SectionInstanceHashResult result = _reconciler.ComputeInstanceContentHash(
            instance, () => Section.FromJson(v1Json));

        Assert.Equal(_normalizer.ComputeContentHash(v1), result.ContentHash);
        Assert.True(result.HasLocalOverrides);
    }

    [Fact]
    public void InstanceHash_LocalInsert_WithTotalBindings_Cancels()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        _ = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), Section.FromJson(v1Json)!);
        AddLocalExtraBlock(instance);

        SectionInstanceHashResult result = _reconciler.ComputeInstanceContentHash(
            instance, () => Section.FromJson(v1Json));

        Assert.Equal(_normalizer.ComputeContentHash(v1), result.ContentHash);
        Assert.True(result.HasLocalOverrides);
    }

    [Fact]
    public void InstanceHash_LocalInsert_NoBindings_Cancels()
    {
        (Section instance, Section v1, string v1Json) = MaterializedUnboundInstance();
        AddLocalExtraBlock(instance);

        SectionInstanceHashResult result = _reconciler.ComputeInstanceContentHash(
            instance, () => Section.FromJson(v1Json));

        Assert.Equal(_normalizer.ComputeContentHash(v1), result.ContentHash);
        Assert.True(result.HasLocalOverrides);
    }

    [Fact]
    public void InstanceHash_LocalInsert_PartialBindings_Cancels()
    {
        Workflow workflow = WorkflowWithTag("metric");
        Section v1 = DetachedSectionWithBlocks("Risk", ("metric", "Compute risk"), ("reader", "Read it"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        instance.SharedComponentBindings = new Dictionary<string, string> { ["metric"] = "metric_2" };
        AddLocalExtraBlock(instance);

        SectionInstanceHashResult result = _reconciler.ComputeInstanceContentHash(
            instance, () => Section.FromJson(v1Json));

        Assert.Equal(_normalizer.ComputeContentHash(v1), result.ContentHash);
        Assert.True(result.HasLocalOverrides);
    }

    [Fact]
    public void InstanceHash_MissingDefinitionSlot_NoRecords_StaysDrift()
    {
        (Section instance, Section v1, string v1Json) = MaterializedUnboundInstance();
        instance.Remove(instance.Blocks.Single(b => b.ReplacementTag == "beta"));
        AddLocalExtraBlock(instance);

        SectionInstanceHashResult result = _reconciler.ComputeInstanceContentHash(
            instance, () => Section.FromJson(v1Json));

        Assert.NotEqual(_normalizer.ComputeContentHash(v1), result.ContentHash);
        Assert.Equal(_reconciler.ComputeInstanceContentHash(instance), result.ContentHash);
        Assert.False(result.HasLocalOverrides);
    }

    [Fact]
    public void InstanceHash_DuplicateInstanceTags_NeverReadInSync()
    {
        (Section instance, Section v1, string v1Json) = MaterializedUnboundInstance();
        _ = new Block(BlockTypes.AiQuery, instance, 1, "alpha")
        {
            Name = "Duplicate tag",
            Properties = new AiQueryProperties { UserPromptTemplate = "Second alpha" },
        };
        AddLocalExtraBlock(instance, 2);

        SectionInstanceHashResult result = _reconciler.ComputeInstanceContentHash(
            instance, () => Section.FromJson(v1Json));

        Assert.NotEqual(_normalizer.ComputeContentHash(v1), result.ContentHash);
    }

    [Fact]
    public void InstanceHash_OverridesWithoutOldDefinition_PlainHashFlagged()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        instance.Remove(instance.Blocks.Single(b => b.ReplacementTag == "beta"));
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["beta"] = SharedComponentSlotState.Hidden,
        };

        SectionInstanceHashResult result = _reconciler.ComputeInstanceContentHash(instance, null);

        Assert.Equal(_reconciler.ComputeInstanceContentHash(instance), result.ContentHash);
        Assert.True(result.HasLocalOverrides);
    }

    [Fact]
    public void Classify_CarriesHasLocalOverrides()
    {
        SharedComponentRelationResult flagged = _reconciler.Classify(
            ActiveComponent(), _headHash, _headHash, _headHash, hasLocalOverrides: true);
        SharedComponentRelationResult plain = _reconciler.Classify(
            ActiveComponent(), _headHash, _headHash, _headHash);

        Assert.True(flagged.HasLocalOverrides);
        Assert.Equal(SharedComponentRelation.InSync, flagged.Relation);
        Assert.False(plain.HasLocalOverrides);
    }

    [Fact]
    public void ApplySection_NewRefSlot_HydratedFromRefHead()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);

        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        v2.Entries =
        [
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
                Bindings = new Dictionary<string, string> { ["b_input"] = "alpha" },
            },
        ];
        Dictionary<string, SharedComponentRefHead> refHeads = RefHeads(
            "component-kyc", "Check {b_input} carefully", new string('e', 64));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2), refHeads);

        Block hydrated = Assert.Single(result.AddedBlocks);
        Assert.Equal("kyc", hydrated.ReplacementTag);
        Assert.Equal("component-kyc", hydrated.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, hydrated.SharedComponentLinkState);
        Assert.Equal(new string('e', 64), hydrated.SharedComponentVersionHash);
        Assert.Equal("alpha", hydrated.SharedComponentBindings!["b_input"]);
        Assert.Equal("Check {alpha} carefully", ((AiQueryProperties)hydrated.Properties).UserPromptTemplate);
        TextInput hydratedInput = Assert.Single(hydrated.TextInputs);
        Assert.Equal("alpha", hydratedInput.ReplacementTag);
        Assert.NotNull(hydrated.StableBlockId);
        List<Block> ordered = [.. instance.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["alpha", "kyc"], ordered.Select(b => b.ReplacementTag));
    }

    [Fact]
    public void ApplySection_MatchedRefSlot_RefreshedToNewHeadWithRenamesReapplied()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        v2.Entries =
        [
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
                Bindings = new Dictionary<string, string> { ["b_input"] = "alpha" },
            },
        ];
        Section v2Definition = ParseDefinition(v2);
        _ = _reconciler.ApplySectionDefinition(
            workflow,
            instance,
            Section.FromJson(v1Json),
            v2Definition,
            RefHeads("component-kyc", "Check {b_input} carefully", new string('e', 64)));
        string kycStableId = instance.Blocks.Single(b => b.ReplacementTag == "kyc").StableBlockId!;

        SectionReconcileResult second = _reconciler.ApplySectionDefinition(
            workflow,
            instance,
            v2Definition,
            ParseDefinition(v2),
            RefHeads("component-kyc", "Check {b_input} twice", new string('f', 64)));

        Assert.Empty(second.AddedBlocks);
        Block refreshed = instance.Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Equal(kycStableId, refreshed.StableBlockId);
        Assert.Equal(new string('f', 64), refreshed.SharedComponentVersionHash);
        Assert.Equal("Check {alpha} twice", ((AiQueryProperties)refreshed.Properties).UserPromptTemplate);
        Assert.Equal("alpha", Assert.Single(refreshed.TextInputs).ReplacementTag);
    }

    [Fact]
    public void ApplySection_RefSlotEditedLocally_ReportsTheSlotAsOverwritten()
    {
        (Workflow workflow, Section instance, Section v2Definition) = SeedMatchedRefSlot();
        Block live = instance.Blocks.Single(b => b.ReplacementTag == "kyc");
        ((AiQueryProperties)live.Properties).UserPromptTemplate = "Locally edited by a prompt engineer";

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow,
            instance,
            v2Definition,
            ParseDefinition(v2Definition),
            RefHeadsAtRealHash("component-kyc", "Check {b_input} carefully"));

        Assert.Equal(["kyc"], result.OverwrittenLocalRefSlots);
    }

    [Fact]
    public void ApplySection_RefSlotUntouchedButHeadMoved_IsACleanUpdateNotAnOverwrite()
    {
        (Workflow workflow, Section instance, Section v2Definition) = SeedMatchedRefSlot();

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow,
            instance,
            v2Definition,
            ParseDefinition(v2Definition),
            RefHeadsAtRealHash("component-kyc", "Check {b_input} twice"));

        Assert.Empty(result.OverwrittenLocalRefSlots);
    }

    [Fact]
    public void ApplySection_BrokenOutRefSlot_IsNeverReportedAsOverwritten()
    {
        (Workflow workflow, Section instance, Section v2Definition) = SeedMatchedRefSlot();
        Block live = instance.Blocks.Single(b => b.ReplacementTag == "kyc");
        ((AiQueryProperties)live.Properties).UserPromptTemplate = "Locally edited then broken out";
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["kyc"] = SharedComponentSlotState.BrokenOut,
        };

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow,
            instance,
            v2Definition,
            ParseDefinition(v2Definition),
            RefHeadsAtRealHash("component-kyc", "Check {b_input} carefully"));

        Assert.Empty(result.OverwrittenLocalRefSlots);
    }

    private (Workflow Workflow, Section Instance, Section V2Definition) SeedMatchedRefSlot()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        v2.Entries =
        [
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
                Bindings = new Dictionary<string, string> { ["b_input"] = "alpha" },
            },
        ];
        Section v2Definition = ParseDefinition(v2);
        _ = _reconciler.ApplySectionDefinition(
            workflow,
            instance,
            Section.FromJson(v1Json),
            v2Definition,
            RefHeadsAtRealHash("component-kyc", "Check {b_input} carefully"));

        return (workflow, instance, v2Definition);
    }

    [Fact]
    public void ApplySection_AddedSlotAlreadyPresentByTagAndContent_ClaimsItInsteadOfDuplicating()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        var localAddition = new Block(BlockTypes.AiQuery, instance, 1, "custom")
        {
            Name = "Shared block",
            Properties = new AiQueryProperties { UserPromptTemplate = "Custom note" },
        };
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("custom", "Custom note"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.AddedBlocks);
        List<Block> ordered = [.. instance.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["alpha", "custom"], ordered.Select(b => b.ReplacementTag));
        Assert.Same(localAddition, ordered[1]);
    }

    [Fact]
    public void ApplySection_AddedSlotTagCollidingWithDifferentLocalContent_LandsUnderUniqueTag()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        _ = new Block(BlockTypes.AiQuery, instance, 1, "custom")
        {
            Name = "Shared block",
            Properties = new AiQueryProperties { UserPromptTemplate = "A genuinely different local note" },
        };
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("custom", "Custom note"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Block landed = Assert.Single(result.AddedBlocks);
        Assert.Equal("custom_2", landed.ReplacementTag);
        Assert.Equal(3, instance.Blocks.Count);
        Assert.Contains(instance.Blocks, b => b.ReplacementTag == "custom"
            && ((AiQueryProperties)b.Properties).UserPromptTemplate == "A genuinely different local note");
    }

    [Fact]
    public void ApplySection_AddedRefSlotWithoutTargetId_DoesNotClaimTheResidentBlock()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        var resident = new Block(BlockTypes.AiQuery, instance, 1, "kyc")
        {
            Name = "Shared block",
            Properties = new AiQueryProperties { UserPromptTemplate = "A local note" },
        };
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        v2.Entries =
        [
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = null },
        ];

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Equal("kyc", Assert.Single(result.DroppedRefSlots));
        Assert.Empty(result.StaleRefSlots);
        Block survivor = instance.Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Same(resident, survivor);
        Assert.True(instance.SharedComponentBindings is null || !instance.SharedComponentBindings.ContainsKey("kyc"));
    }

    [Fact]
    public void ApplySection_AddedInlineSlotMatchingALinkedResident_DoesNotClaimIt()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        var linkedResident = new Block(BlockTypes.AiQuery, instance, 1, "note")
        {
            Name = "Shared block",
            Properties = new AiQueryProperties { UserPromptTemplate = "Coincidental content" },
        };
        linkedResident.SharedComponentId = "component-other";
        linkedResident.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linkedResident.SharedComponentVersionHash = new string('c', 64);
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("note", "Coincidental content"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Block landed = Assert.Single(result.AddedBlocks);
        Assert.Equal("note_2", landed.ReplacementTag);
        Block keptResident = instance.Blocks.Single(b => b.ReplacementTag == "note");
        Assert.Same(linkedResident, keptResident);
        Assert.Equal("component-other", keptResident.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, keptResident.SharedComponentLinkState);
    }

    [Fact]
    public void ApplySection_AddedRefSlotAlreadyLinkedInInstance_ClaimsInsteadOfDuplicating()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        var linkedInsert = new Block(BlockTypes.AiQuery, instance, 1, "kyc")
        {
            Name = "KYC Check",
            Properties = new AiQueryProperties { UserPromptTemplate = "Check {b_input} carefully" },
        };
        linkedInsert.SharedComponentId = "component-kyc";
        linkedInsert.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linkedInsert.SharedComponentVersionHash = new string('d', 64);
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        v2.Entries =
        [
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = "component-kyc" },
        ];

        SectionReconcileResult second = _reconciler.ApplySectionDefinition(
            workflow,
            instance,
            Section.FromJson(v1Json),
            ParseDefinition(v2),
            RefHeads("component-kyc", "Check {b_input} carefully", new string('e', 64)));

        Assert.Empty(second.AddedBlocks);
        List<Block> ordered = [.. instance.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["alpha", "kyc"], ordered.Select(b => b.ReplacementTag));
        Assert.Same(linkedInsert, ordered[1]);
        Assert.Equal(new string('e', 64), ordered[1].SharedComponentVersionHash);
    }

    [Fact]
    public void ApplySection_NewRefSlotWithoutResolvableHead_ReportsDroppedRefSlot()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        v2.Entries =
        [
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
                Bindings = new Dictionary<string, string> { ["b_input"] = "alpha" },
            },
        ];

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2), refHeads: null);

        Assert.Empty(result.AddedBlocks);
        Assert.Equal("kyc", Assert.Single(result.DroppedRefSlots));
        Assert.Empty(result.StaleRefSlots);
        Assert.Equal("alpha", Assert.Single(instance.Blocks).ReplacementTag);
    }

    [Fact]
    public void ApplySection_MatchedRefSlotWithoutResolvableHead_KeepsCopyAndReportsStaleRefSlot()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        v2.Entries =
        [
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
                Bindings = new Dictionary<string, string> { ["b_input"] = "alpha" },
            },
        ];
        Section v2Definition = ParseDefinition(v2);
        _ = _reconciler.ApplySectionDefinition(
            workflow,
            instance,
            Section.FromJson(v1Json),
            v2Definition,
            RefHeads("component-kyc", "Check {b_input} carefully", new string('e', 64)));

        SectionReconcileResult second = _reconciler.ApplySectionDefinition(
            workflow, instance, v2Definition, ParseDefinition(v2), refHeads: null);

        Assert.Equal("kyc", Assert.Single(second.StaleRefSlots));
        Assert.Empty(second.DroppedRefSlots);
        Block kept = instance.Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Equal(new string('e', 64), kept.SharedComponentVersionHash);
    }

    [Fact]
    public void ApplySection_BrokenOutRefSlotWithoutResolvableHead_IsNotReportedStale()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"));
        v2.Entries =
        [
            new SectionEntry { SlotTag = "alpha", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
                Bindings = new Dictionary<string, string> { ["b_input"] = "alpha" },
            },
        ];
        Section v2Definition = ParseDefinition(v2);
        _ = _reconciler.ApplySectionDefinition(
            workflow,
            instance,
            Section.FromJson(v1Json),
            v2Definition,
            RefHeads("component-kyc", "Check {b_input} carefully", new string('e', 64)));
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["kyc"] = SharedComponentSlotState.BrokenOut,
        };

        SectionReconcileResult second = _reconciler.ApplySectionDefinition(
            workflow, instance, v2Definition, ParseDefinition(v2), refHeads: null);

        Assert.Empty(second.StaleRefSlots);
        Assert.Empty(second.DroppedRefSlots);
        Assert.Contains(instance.Blocks, b => b.ReplacementTag == "kyc");
    }

    [Fact]
    public void ApplySection_RemovedBrokenOutSlot_SurvivesAsPreservedLocal()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        Block fork = instance.Blocks.Single(b => b.ReplacementTag == "beta");
        ((AiQueryProperties)fork.Properties).UserPromptTemplate = "My local fork";
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["beta"] = SharedComponentSlotState.BrokenOut,
        };

        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt v2"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.RemovedBlocks);
        Assert.Same(fork, Assert.Single(result.PreservedLocalBlocks));
        List<Block> ordered = [.. instance.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["alpha", "beta"], ordered.Select(b => b.ReplacementTag));
        Assert.Equal("My local fork", ((AiQueryProperties)ordered[1].Properties).UserPromptTemplate);
        Assert.Null(instance.SharedComponentSlotStates);
    }

    [Fact]
    public void ApplySection_RemovedHiddenSlot_TombstoneGarbageCollected()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        instance.Remove(instance.Blocks.Single(b => b.ReplacementTag == "beta"));
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["beta"] = SharedComponentSlotState.Hidden,
        };

        Section v2 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt v2"));

        SectionReconcileResult result = _reconciler.ApplySectionDefinition(
            workflow, instance, Section.FromJson(v1Json), ParseDefinition(v2));

        Assert.Empty(result.RemovedBlocks);
        Assert.Empty(result.AddedBlocks);
        Assert.Equal("alpha", Assert.Single(instance.Blocks).ReplacementTag);
        Assert.Null(instance.SharedComponentSlotStates);
    }

    [Fact]
    public void Materialize_SectionWithRefEntry_HydratesInnerAndStampsTotalBindings()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        var editor = new SharedComponentEditor(_canonicalizer, _normalizer);
        Section defSource = DetachedSectionWithBlocks("Onboarding", ("intro", "Intro prompt"));
        defSource.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "kyc",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = "component-kyc",
                Bindings = new Dictionary<string, string> { ["b_input"] = "intro" },
            },
        ];
        string defJson = _normalizer.ToDefinitionJson(defSource);
        Dictionary<string, SharedComponentRefHead> refHeads = RefHeads(
            "component-kyc", "Check {b_input} carefully", new string('e', 64));

        ErrorOr<MaterializedNode> landed = editor.Materialize(
            workflow,
            SharedComponentKind.Section,
            COMPONENT_ID,
            "Onboarding",
            _normalizer.ComputeContentHashOfDefinitionJson(defJson),
            defJson,
            null,
            null,
            null,
            refHeads);

        Assert.False(landed.IsError);
        Section section = workflow.Sections.OrderBy(s => s.Order).Last();
        List<Block> ordered = [.. section.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["intro", "kyc"], ordered.Select(b => b.ReplacementTag));
        Block inner = ordered[1];
        Assert.Equal("component-kyc", inner.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, inner.SharedComponentLinkState);
        Assert.Equal(new string('e', 64), inner.SharedComponentVersionHash);
        Assert.Equal("intro", inner.SharedComponentBindings!["b_input"]);
        Assert.Equal("Check {intro} carefully", ((AiQueryProperties)inner.Properties).UserPromptTemplate);
        Assert.NotNull(inner.StableBlockId);
        Assert.Equal("intro", section.SharedComponentBindings!["intro"]);
        Assert.Equal("kyc", section.SharedComponentBindings["kyc"]);
        Assert.Equal(SharedComponentLinkState.Linked, section.SharedComponentLinkState);
    }

    private static Dictionary<string, SharedComponentRefHead> RefHeadsAtRealHash(string componentId, string prompt)
    {
        var scratch = new Workflow("scratch");
        var scratchSection = new Section("scratch", scratch);
        var definition = new Block(BlockTypes.AiQuery, scratchSection, "slot")
        {
            Name = "KYC Check",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
        _ = new TextInput(definition, "b_input", "seed");
        var normalizer = new SharedComponentContentNormalizer(new WorkflowCanonicalizer());
        Block parsed = Block.FromJson(normalizer.ToDefinitionJson(definition))!;

        return new Dictionary<string, SharedComponentRefHead>
        {
            [componentId] = new SharedComponentRefHead(normalizer.ComputeContentHash(parsed), parsed),
        };
    }

    private static Dictionary<string, SharedComponentRefHead> RefHeads(
        string componentId,
        string prompt,
        string contentHash)
    {
        var scratch = new Workflow("scratch");
        var scratchSection = new Section("scratch", scratch);
        var definition = new Block(BlockTypes.AiQuery, scratchSection, "slot")
        {
            Name = "KYC Check",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
        _ = new TextInput(definition, "b_input", "seed");
        var normalizer = new SharedComponentContentNormalizer(new WorkflowCanonicalizer());
        Block parsed = Block.FromJson(normalizer.ToDefinitionJson(definition))!;

        return new Dictionary<string, SharedComponentRefHead>
        {
            [componentId] = new SharedComponentRefHead(contentHash, parsed),
        };
    }

    private static SharedComponent ActiveComponent()
    {
        return new SharedComponent { Kind = SharedComponentKind.Section, Name = "Alpha" };
    }

    private Section ParseDefinition(Section source)
    {
        return Section.FromJson(_normalizer.ToDefinitionJson(source))!;
    }

    private Section MaterializeSection(Workflow workflow, Section source, string definitionJson)
    {
        var editor = new SharedComponentEditor(_canonicalizer, _normalizer);
        ErrorOr<MaterializedNode> landed = editor.Materialize(
            workflow, SharedComponentKind.Section, COMPONENT_ID, source.Name,
            _normalizer.ComputeContentHash(source), definitionJson, null, null, null);
        Assert.False(landed.IsError);

        return workflow.Sections.OrderBy(s => s.Order).Last();
    }

    private (Section Instance, Section Definition, string DefinitionJson) MaterializedUnboundInstance()
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        _ = new Section("S1", workflow) { Title = "S1" };
        Section v1 = DetachedSectionWithBlocks("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        string v1Json = _normalizer.ToDefinitionJson(v1);
        Section instance = MaterializeSection(workflow, v1, v1Json);
        instance.SharedComponentBindings = null;

        return (instance, v1, v1Json);
    }

    private static void AddLocalExtraBlock(Section instance, int order = 1)
    {
        _ = new Block(BlockTypes.AiQuery, instance, order, "custom")
        {
            Name = "Local extra",
            Properties = new AiQueryProperties { UserPromptTemplate = "Custom note" },
        };
    }

    private static Section DetachedSectionWithBlocks(string title, params (string Tag, string Prompt)[] blocks)
    {
        var scratch = new Workflow("scratch");
        var section = new Section(title, scratch) { Title = title };
        foreach ((string tag, string prompt) in blocks)
        {
            _ = new Block(BlockTypes.AiQuery, section, tag)
            {
                Name = "Shared block",
                Properties = new AiQueryProperties { UserPromptTemplate = prompt },
            };
        }

        return section;
    }

    private static SharedComponentVersion Version(int sequenceNumber, string contentHash, bool payloadAbsent = false)
    {
        var version = new SharedComponentVersion
        {
            SharedComponentId = COMPONENT_ID,
            SequenceNumber = sequenceNumber,
            ContentHash = contentHash,
        };
        if (payloadAbsent)
        {
            version.PayloadAbsentAt = DateTime.UtcNow;
        }
        else
        {
            version.ContentJson = $$"""{"v":{{sequenceNumber}}}""";
        }

        return version;
    }

    private static Workflow WorkflowWithTag(string tag)
    {
        var workflow = new Workflow("Report") { Title = "Report", WorkflowTypeId = "WT" };
        var section = new Section("S1", workflow) { Title = "S1" };
        _ = new Block(BlockTypes.AiQuery, section, tag)
        {
            Name = $"Block {tag}",
            Properties = new AiQueryProperties { UserPromptTemplate = $"Prompt {tag}" },
        };

        return workflow;
    }

    private static Section DetachedSection(string title, string innerTag)
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
