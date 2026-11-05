using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetWorkflowSharedComponentReconcile;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using static IniaAi.Application.UnitTests.Common.Workflows.SharedComponentLinkTestData;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class GetWorkflowSharedComponentReconcileQueryHandlerTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string COMPONENT_ID = "component-1";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly CoreDbContext _coreContext;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly GetWorkflowSharedComponentReconcileQueryHandler _handler;

    public GetWorkflowSharedComponentReconcileQueryHandlerTests()
    {
        _coreContext = _harness.Context;
        _coreUnitOfWork = _harness.UnitOfWork;
        _handler = new GetWorkflowSharedComponentReconcileQueryHandler(
            _coreUnitOfWork,
            new SharedComponentReconciler(_harness.Normalizer),
            _harness.Normalizer);
    }

    [Fact]
    public async Task Reconcile_LibraryVersionStoredWithAFieldTheModelDropped_StaysInSync()
    {
        (string json, string hash) = _harness.StoredBeforeAFieldWasDropped(
            Definition(BuildBlockDefinition("Base prompt")).DefinitionJson);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Rates", id: "c-dropped");
        _ = await _harness.AddVersionAsync("c-dropped", 1, hash, json);
        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-DROP" };
        var section = new Section("Main", workflow) { Title = "Main" };
        _ = LinkedBlock(section, "rates", "Base prompt", "c-dropped", hash);
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-DROP"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(SharedComponentRelation.InSync, Assert.Single(result.Value.Instances).Relation);
    }

    [Fact]
    public async Task Reconcile_ClassifiesLinkedBlockInstancesAcrossAllRelations()
    {
        (string defJsonBase, string hashBase) = Definition(BuildBlockDefinition("Base prompt"));
        (string defJsonMoved, string hashMoved) = Definition(BuildBlockDefinition("Moved prompt"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "In Sync", id: "c-insync");
        SharedComponentVersion inSyncHead = await _harness.AddVersionAsync("c-insync", 1, hashBase, defJsonBase);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Head Ahead", id: "c-headahead");
        _ = await _harness.AddVersionAsync("c-headahead", 1, hashBase, defJsonBase);
        _ = await _harness.AddVersionAsync("c-headahead", 2, hashMoved, defJsonMoved);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Local Ahead", id: "c-localahead");
        _ = await _harness.AddVersionAsync("c-localahead", 1, hashBase, defJsonBase);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Diverged", id: "c-diverged");
        _ = await _harness.AddVersionAsync("c-diverged", 1, hashBase, defJsonBase);
        _ = await _harness.AddVersionAsync("c-diverged", 2, hashMoved, defJsonMoved);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Retired", SharedComponentState.Retired, id: "c-retired");
        _ = await _harness.AddVersionAsync("c-retired", 1, hashBase, defJsonBase);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-R1" };
        var section = new Section("Main", workflow) { Title = "Main" };
        _ = LinkedBlock(section, "a1", "Base prompt", "c-insync", hashBase);
        _ = LinkedBlock(section, "a2", "Base prompt", "c-headahead", hashBase);
        _ = LinkedBlock(section, "a3", "Local twist", "c-localahead", hashBase);
        _ = LinkedBlock(section, "a4", "Local twist", "c-diverged", hashBase);
        _ = LinkedBlock(section, "a5", "Base prompt", "c-ghost", hashBase);
        _ = LinkedBlock(section, "a6", "Base prompt", "c-retired", hashBase);
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-R1"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal("WT-R1", result.Value.WorkflowTypeId);
        Assert.Equal(6, result.Value.Instances.Count);

        SharedComponentInstanceReconcileRow inSync =
            result.Value.Instances.Single(r => r.SharedComponentId == "c-insync");
        Assert.Equal(SharedComponentKind.Block, inSync.Kind);
        Assert.Equal(SharedComponentLinkState.Linked, inSync.LinkState);
        Assert.Equal(SharedComponentRelation.InSync, inSync.Relation);
        Assert.Equal("In Sync", inSync.ComponentName);
        Assert.Equal(SharedComponentState.Active, inSync.ComponentState);
        Assert.Equal(hashBase, inSync.InstanceContentHash);
        Assert.Equal(hashBase, inSync.LinkVersionHash);
        Assert.Equal(inSyncHead.Id, inSync.HeadVersionId);
        Assert.Equal(hashBase, inSync.HeadContentHash);
        Assert.False(inSync.LinkHashStale);
        Assert.Equal(
            new[] { SharedComponentReconcileAction.BreakOut, SharedComponentReconcileAction.KeepLocal },
            inSync.AvailableActions);

        SharedComponentInstanceReconcileRow headAhead =
            result.Value.Instances.Single(r => r.SharedComponentId == "c-headahead");
        Assert.Equal(SharedComponentRelation.HeadAhead, headAhead.Relation);
        Assert.Equal(hashMoved, headAhead.HeadContentHash);
        Assert.Equal(
            new[]
            {
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.BreakOut,
                SharedComponentReconcileAction.KeepLocal,
            },
            headAhead.AvailableActions);

        SharedComponentInstanceReconcileRow localAhead =
            result.Value.Instances.Single(r => r.SharedComponentId == "c-localahead");
        Assert.Equal(SharedComponentRelation.LocalAhead, localAhead.Relation);
        Assert.Equal(
            new[]
            {
                SharedComponentReconcileAction.UpdateShared,
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.BreakOut,
                SharedComponentReconcileAction.KeepLocal,
            },
            localAhead.AvailableActions);

        SharedComponentInstanceReconcileRow diverged =
            result.Value.Instances.Single(r => r.SharedComponentId == "c-diverged");
        Assert.Equal(SharedComponentRelation.Diverged, diverged.Relation);
        Assert.Equal(
            new[]
            {
                SharedComponentReconcileAction.UpdateShared,
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.BreakOut,
                SharedComponentReconcileAction.KeepLocal,
            },
            diverged.AvailableActions);

        SharedComponentInstanceReconcileRow missing =
            result.Value.Instances.Single(r => r.SharedComponentId == "c-ghost");
        Assert.Equal(SharedComponentRelation.ComponentMissing, missing.Relation);
        Assert.Null(missing.ComponentName);
        Assert.Null(missing.ComponentState);
        Assert.Null(missing.HeadVersionId);
        Assert.Null(missing.HeadContentHash);
        Assert.Equal(
            new[] { SharedComponentReconcileAction.BreakOut, SharedComponentReconcileAction.KeepLocal },
            missing.AvailableActions);

        SharedComponentInstanceReconcileRow retired =
            result.Value.Instances.Single(r => r.SharedComponentId == "c-retired");
        Assert.Equal(SharedComponentRelation.ComponentRetired, retired.Relation);
        Assert.Equal(SharedComponentState.Retired, retired.ComponentState);
        Assert.Equal(
            new[] { SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.BreakOut },
            retired.AvailableActions);
    }

    [Fact]
    public async Task Reconcile_SignatureFieldsHoistedOffInstances_ReadInSync()
    {
        Block sigBlockDef = BuildBlockDefinition("Base prompt");
        DeclareDirectorSignature(sigBlockDef);
        (string blockDefJson, string blockHash) = Definition(sigBlockDef);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Sig Block", id: "c-sig-block");
        _ = await _harness.AddVersionAsync("c-sig-block", 1, blockHash, blockDefJson);

        Section sigSectionDef = DefSection("Market Data", ("rate", "Extract the FX rate"), ("source", "Cite the source"));
        DeclareDirectorSignature(sigSectionDef.Blocks.Single(b => b.ReplacementTag == "source"));
        (string sectionDefJson, string sectionHash) = Definition(sigSectionDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Sig Section", id: "c-sig-sec");
        _ = await _harness.AddVersionAsync("c-sig-sec", 1, sectionHash, sectionDefJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-SIG" };
        var host = new Section("Main", workflow) { Title = "Main" };
        _ = LinkedBlock(host, "b1", "Base prompt", "c-sig-block", blockHash);
        _ = LinkedBlock(host, "b2", "Local twist", "c-sig-block", blockHash);
        var linked = new Section("Market Data", workflow) { Title = "Market Data" };
        _ = new Block(BlockTypes.AiQuery, linked, "rate")
        {
            Name = "Rate",
            Properties = new AiQueryProperties { UserPromptTemplate = "Extract the FX rate" },
        };
        _ = new Block(BlockTypes.AiQuery, linked, "source")
        {
            Name = "Source",
            Properties = new AiQueryProperties { UserPromptTemplate = "Cite the source" },
        };
        linked.SharedComponentId = "c-sig-sec";
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentVersionHash = sectionHash;
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-SIG"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow cleanBlock = result.Value.Instances
            .Single(r => r.SharedComponentId == "c-sig-block" && r.TargetPath.EndsWith("Blocks[0]"));
        Assert.Equal(SharedComponentRelation.InSync, cleanBlock.Relation);
        SharedComponentInstanceReconcileRow editedBlock = result.Value.Instances
            .Single(r => r.SharedComponentId == "c-sig-block" && r.TargetPath.EndsWith("Blocks[1]"));
        Assert.Equal(SharedComponentRelation.LocalAhead, editedBlock.Relation);
        SharedComponentInstanceReconcileRow sectionRow = result.Value.Instances
            .Single(r => r.SharedComponentId == "c-sig-sec");
        Assert.Equal(SharedComponentRelation.InSync, sectionRow.Relation);
        SharedComponentEntryReconcileRow sourceEntry = sectionRow.Entries!
            .Single(e => e.EntryKey == "source");
        Assert.Equal(SharedComponentRelation.InSync, sourceEntry.Relation);
    }

    private static void DeclareDirectorSignature(Block block)
    {
        ((AiQueryProperties)block.Properties).SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["director_name"] = new("Director", "Who signs", "N/A"),
        };
    }

    [Fact]
    public async Task Reconcile_BrokenOutInstances_OfferRelinkOnlyWhenCleanVsHead()
    {
        (string defJson, string hash) = Definition(BuildBlockDefinition("Base prompt"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, defJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-R2" };
        var section = new Section("Main", workflow) { Title = "Main" };
        _ = LinkedBlock(section, "b1", "Base prompt", COMPONENT_ID, hash, SharedComponentLinkState.BrokenOut);
        _ = LinkedBlock(section, "b2", "Forked twist", COMPONENT_ID, hash, SharedComponentLinkState.BrokenOut);
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-R2"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Instances.Count);

        SharedComponentInstanceReconcileRow clean =
            result.Value.Instances.Single(r => r.TargetPath == "Sections[0].Blocks[0]");
        Assert.Equal(SharedComponentLinkState.BrokenOut, clean.LinkState);
        Assert.Equal(SharedComponentRelation.InSync, clean.Relation);
        Assert.Equal(
            new[] { SharedComponentReconcileAction.Relink, SharedComponentReconcileAction.KeepLocal },
            clean.AvailableActions);

        SharedComponentInstanceReconcileRow forked =
            result.Value.Instances.Single(r => r.TargetPath == "Sections[0].Blocks[1]");
        Assert.Equal(SharedComponentRelation.LocalAhead, forked.Relation);
        Assert.Equal(
            new[]
            {
                SharedComponentReconcileAction.UpdateShared,
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.KeepLocal,
            },
            forked.AvailableActions);
    }

    [Fact]
    public async Task Reconcile_SectionInstance_ReverseAppliesBindings_AndFlagsStaleLinkHash()
    {
        (string defJsonV1, string hashV1) = Definition(
            DefSection("Market Data", ("rate", "Extract the FX rate")));
        (string defJsonV2, string hashV2) = Definition(
            DefSection("Market Data", ("rate", "Extract the FX rate"), ("source", "Cite the source")));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, defJsonV1);
        SharedComponentVersion head = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, defJsonV2);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-R3" };
        Section instance = DefSection(
            "Market Data",
            workflow,
            ("rate_2", "Extract the FX rate"),
            ("source", "Cite the source"));
        instance.SharedComponentId = COMPONENT_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = hashV1;
        instance.SharedComponentBindings = new Dictionary<string, string> { ["rate"] = "rate_2" };
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-R3"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow row = Assert.Single(result.Value.Instances);
        Assert.Equal(SharedComponentKind.Section, row.Kind);
        Assert.Equal("Sections[0]", row.TargetPath);
        Assert.Equal(instance.StableSectionId, row.TargetStableId);
        Assert.Equal(hashV2, row.InstanceContentHash);
        Assert.Equal(SharedComponentRelation.InSync, row.Relation);
        Assert.True(row.LinkHashStale);
        Assert.Equal(head.Id, row.HeadVersionId);
        Assert.True(row.OldDefinitionAvailable);
    }

    [Fact]
    public async Task Reconcile_OldDefinitionAvailability_RequiresPayloadPresentChainRow()
    {
        (_, string hashV1) = Definition(BuildBlockDefinition("Base prompt"));
        (string defJsonV2, string hashV2) = Definition(BuildBlockDefinition("Moved prompt"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, null, payloadAbsentAt: DateTime.UtcNow);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, defJsonV2);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-R4" };
        var section = new Section("Main", workflow) { Title = "Main" };
        _ = LinkedBlock(section, "x1", "Base prompt", COMPONENT_ID, hashV1);
        _ = LinkedBlock(section, "x2", "Base prompt", COMPONENT_ID, new string('f', 64));
        _ = LinkedBlock(section, "x3", "Moved prompt", COMPONENT_ID, hashV2);
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-R4"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.Instances.Single(r => r.LinkVersionHash == hashV1).OldDefinitionAvailable);
        Assert.False(result.Value.Instances
            .Single(r => r.LinkVersionHash == new string('f', 64)).OldDefinitionAvailable);
        Assert.True(result.Value.Instances.Single(r => r.LinkVersionHash == hashV2).OldDefinitionAvailable);
    }

    [Fact]
    public async Task Reconcile_BuildsOrderSortedAddresses_AndSkipsUnstampedNodes()
    {
        (string defJsonSection, string hashSection) = Definition(
            DefSection("Late", ("late_note", "Summarize")));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Late", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashSection, defJsonSection);
        (string defJsonBlock, string hashBlock) = Definition(BuildBlockDefinition("Base prompt"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer", id: "c-block");
        _ = await _harness.AddVersionAsync("c-block", 1, hashBlock, defJsonBlock);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-R5" };
        var early = new Section("Early", workflow) { Title = "Early" };
        Section late = DefSection("Late", workflow, ("late_note", "Summarize"));
        early.Order = 5;
        late.Order = 10;
        late.SharedComponentId = COMPONENT_ID;
        late.SharedComponentLinkState = SharedComponentLinkState.Linked;
        late.SharedComponentVersionHash = hashSection;
        var plain = new Block(BlockTypes.AiQuery, early, "plain")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "Plain" },
        };
        plain.Order = 3;
        plain.SharedComponentId = "c-unstamped";
        plain.SharedComponentLinkState = null;
        plain.SharedComponentVersionHash = new string('a', 64);
        Block linkedBlock = LinkedBlock(early, "linked_late", "Base prompt", "c-block", hashBlock);
        linkedBlock.Order = 7;
        await _harness.AddWorkflowAsync(workflow);
        var older = new WorkflowVersion
        {
            WorkflowTypeId = "WT-R5",
            ContentHash = new string('1', 64),
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            CreatedByUserId = USER,
        };
        var newer = new WorkflowVersion
        {
            WorkflowTypeId = "WT-R5",
            ContentHash = new string('2', 64),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            CreatedByUserId = USER,
        };
        _ = _coreContext.Add(older);
        _ = _coreContext.Add(newer);
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-R5"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(newer.Id, result.Value.BaseVersionId);
        Assert.Equal(new string('2', 64), result.Value.BaseContentHash);
        Assert.Equal(2, result.Value.Instances.Count);
        SharedComponentInstanceReconcileRow blockRow =
            result.Value.Instances.Single(r => r.Kind == SharedComponentKind.Block);
        Assert.Equal("Sections[0].Blocks[1]", blockRow.TargetPath);
        Assert.Equal(linkedBlock.StableBlockId, blockRow.TargetStableId);
        SharedComponentInstanceReconcileRow sectionRow =
            result.Value.Instances.Single(r => r.Kind == SharedComponentKind.Section);
        Assert.Equal("Sections[1]", sectionRow.TargetPath);
        Assert.Equal(late.StableSectionId, sectionRow.TargetStableId);
    }

    [Fact]
    public async Task Reconcile_LinkedSectionWithFourEntryStates_AssemblesEntryRowsAndExcludesInnerFromTopLevel()
    {
        (string kycV1Json, string kycV1Hash) = Definition(BuildBlockDefinition("KYC v1"));
        (string kycV2Json, string kycV2Hash) = Definition(BuildBlockDefinition("KYC v2"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: "c-kyc");
        _ = await _harness.AddVersionAsync("c-kyc", 1, kycV1Hash, kycV1Json);
        SharedComponentVersion kycHead = await _harness.AddVersionAsync("c-kyc", 2, kycV2Hash, kycV2Json);

        Section headDef = DefSection(
            "Risk", ("intro", "Intro prompt"), ("extra", "Extra prompt"));
        headDef.Blocks.Single(b => b.ReplacementTag == "intro").Name = "Intro Analysis";
        headDef.Blocks.Single(b => b.ReplacementTag == "extra").Name = "Extra Analysis";
        headDef.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = "c-kyc" },
            new SectionEntry { SlotTag = "extra", Kind = SectionEntryKind.Inline },
        ];
        (string secJson, string secHash) = Definition(headDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        SharedComponentVersion secHead = await _harness.AddVersionAsync("c-sec", 1, secHash, secJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-E1" };
        var section = new Section("Main", workflow) { Title = "Main" };
        var intro = new Block(BlockTypes.AiQuery, section, "intro")
        {
            Name = "Intro Analysis",
            Properties = new AiQueryProperties { UserPromptTemplate = "Broken content" },
        };
        _ = LinkedBlock(section, "kyc", "KYC v1", "c-kyc", kycV1Hash);
        _ = new Block(BlockTypes.AiQuery, section, "local_note")
        {
            Name = "Note",
            Properties = new AiQueryProperties { UserPromptTemplate = "Local note" },
        };
        section.SharedComponentId = "c-sec";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = secHash;
        section.SharedComponentBindings = new Dictionary<string, string>
        {
            ["intro"] = "intro",
            ["kyc"] = "kyc",
        };
        section.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["intro"] = SharedComponentSlotState.BrokenOut,
            ["extra"] = SharedComponentSlotState.Hidden,
        };
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-E1"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow row = Assert.Single(result.Value.Instances);
        Assert.Equal(SharedComponentKind.Section, row.Kind);
        Assert.False(row.OrderDeviates);
        Assert.NotNull(row.Entries);
        Assert.Equal(4, row.Entries.Count);

        SharedComponentEntryReconcileRow introRow = row.Entries[0];
        Assert.Equal("intro", introRow.EntryKey);
        Assert.Equal(SharedComponentEntryOrigin.DefinitionInline, introRow.Origin);
        Assert.Equal(SharedComponentEntryOverrideState.BrokenOut, introRow.OverrideState);
        Assert.Equal(intro.StableBlockId, introRow.EntryStableId);
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.AdoptHead],
            introRow.AvailableActions);

        SharedComponentEntryReconcileRow kycRow = row.Entries[1];
        Assert.Equal("kyc", kycRow.EntryKey);
        Assert.Equal(SharedComponentEntryOrigin.DefinitionRef, kycRow.Origin);
        Assert.Equal(SharedComponentEntryOverrideState.None, kycRow.OverrideState);
        Assert.Equal("c-kyc", kycRow.RefSharedComponentId);
        Assert.Equal("KYC Check", kycRow.RefComponentName);
        Assert.Equal(kycHead.Id, kycRow.RefHeadVersionId);
        Assert.Equal(SharedComponentRelation.HeadAhead, kycRow.Relation);
        Assert.Equal(
            [
                SharedComponentReconcileAction.AdoptHead,
                SharedComponentReconcileAction.BreakOut,
                SharedComponentReconcileAction.KeepLocal,
            ],
            kycRow.AvailableActions);

        SharedComponentEntryReconcileRow extraRow = row.Entries[2];
        Assert.Equal("extra", extraRow.EntryKey);
        Assert.Equal(SharedComponentEntryOverrideState.Hidden, extraRow.OverrideState);
        Assert.Null(extraRow.EntryStableId);
        Assert.Null(extraRow.InstanceContentHash);
        Assert.Equal("Extra Analysis", extraRow.DisplayName);
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.Unhide],
            extraRow.AvailableActions);

        SharedComponentEntryReconcileRow insertRow = row.Entries[3];
        Assert.Null(insertRow.EntryKey);
        Assert.Equal(SharedComponentEntryOrigin.LocalInsert, insertRow.Origin);
        Assert.False(insertRow.DeletedByDefinition);
        Assert.Equal(
            [SharedComponentReconcileAction.KeepLocal, SharedComponentReconcileAction.UpdateShared],
            insertRow.AvailableActions);
        Assert.Equal(secHead.Id, result.Value.Instances[0].HeadVersionId);
        Assert.True(row.HasLocalOverrides);
        Assert.Equal(1, row.LocalInsertCount);
    }

    [Fact]
    public async Task Reconcile_InSyncSectionWithHiddenSlotAndLocalInsert_ThreadsOverridesAndInsertCount()
    {
        Section headDef = DefSection("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        (string secJson, string secHash) = Definition(headDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        _ = await _harness.AddVersionAsync("c-sec", 1, secHash, secJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-B2" };
        var section = new Section("Risk", workflow) { Title = "Risk" };
        _ = new Block(BlockTypes.AiQuery, section, "alpha")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "Alpha prompt" },
        };
        _ = new Block(BlockTypes.AiQuery, section, "custom")
        {
            Name = "Local extra",
            Properties = new AiQueryProperties { UserPromptTemplate = "Custom note" },
        };
        section.SharedComponentId = "c-sec";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = secHash;
        section.SharedComponentBindings = new Dictionary<string, string>
        {
            ["alpha"] = "alpha",
            ["beta"] = "beta",
        };
        section.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["beta"] = SharedComponentSlotState.Hidden,
        };
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-B2"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow row = Assert.Single(result.Value.Instances);
        Assert.Equal(SharedComponentRelation.InSync, row.Relation);
        Assert.True(row.HasLocalOverrides);
        Assert.Equal(1, row.LocalInsertCount);
    }

    [Fact]
    public async Task Reconcile_CleanSectionWithLocalInsert_ReadsInSyncWithInsertCount()
    {
        Section headDef = DefSection("Risk", ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        (string secJson, string secHash) = Definition(headDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        _ = await _harness.AddVersionAsync("c-sec", 1, secHash, secJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-B3" };
        Section section = DefSection("Risk", workflow, ("alpha", "Alpha prompt"), ("beta", "Beta prompt"));
        _ = new Block(BlockTypes.AiQuery, section, "custom")
        {
            Name = "Local extra",
            Properties = new AiQueryProperties { UserPromptTemplate = "Custom note" },
        };
        section.SharedComponentId = "c-sec";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = secHash;
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-B3"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow row = Assert.Single(result.Value.Instances);
        Assert.Equal(SharedComponentRelation.InSync, row.Relation);
        Assert.Equal(1, row.LocalInsertCount);
        _ = Assert.Single(row.Entries!, e => e.Origin == SharedComponentEntryOrigin.LocalInsert);
    }

    [Fact]
    public async Task Reconcile_TwoVersionSectionChain_ClassifiesInlineEntryRelationsPerSlot()
    {
        Section v1Def = DefSection("Risk", ("a", "A stable"), ("b", "B v1"), ("c", "C v1"), ("d", "D stable"));
        (string v1Json, string v1Hash) = Definition(v1Def);
        Section v2Def = DefSection("Risk", ("a", "A stable"), ("b", "B v2"), ("c", "C v2"), ("d", "D stable"));
        (string v2Json, string v2Hash) = Definition(v2Def);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        _ = await _harness.AddVersionAsync("c-sec", 1, v1Hash, v1Json);
        _ = await _harness.AddVersionAsync("c-sec", 2, v2Hash, v2Json);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-CH" };
        var section = new Section("Risk", workflow) { Title = "Risk" };
        AddInline(section, "a", "A stable");
        AddInline(section, "b", "B v1");
        AddInline(section, "c", "C local");
        AddInline(section, "d", "D local");
        section.SharedComponentId = "c-sec";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = v1Hash;
        section.SharedComponentBindings = new Dictionary<string, string>
        {
            ["a"] = "a",
            ["b"] = "b",
            ["c"] = "c",
            ["d"] = "d",
        };
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-CH"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow row = Assert.Single(result.Value.Instances);
        Assert.NotNull(row.Entries);
        Assert.Equal(4, row.Entries.Count);
        Assert.Equal(SharedComponentRelation.InSync, row.Entries[0].Relation);
        Assert.Equal(SharedComponentRelation.HeadAhead, row.Entries[1].Relation);
        Assert.Equal(SharedComponentRelation.Diverged, row.Entries[2].Relation);
        Assert.Equal(SharedComponentRelation.LocalAhead, row.Entries[3].Relation);
    }

    [Fact]
    public async Task Reconcile_UnresolvableOldDefinition_ClassifiesEditedInlineEntryDiverged()
    {
        Section headDef = DefSection("Risk", ("a", "A head"));
        (string secJson, string secHash) = Definition(headDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        _ = await _harness.AddVersionAsync("c-sec", 1, secHash, secJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-NO" };
        var section = new Section("Risk", workflow) { Title = "Risk" };
        AddInline(section, "a", "A local edit");
        section.SharedComponentId = "c-sec";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = new string('f', 64);
        section.SharedComponentBindings = new Dictionary<string, string> { ["a"] = "a" };
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-NO"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow row = Assert.Single(result.Value.Instances);
        SharedComponentEntryReconcileRow entry = Assert.Single(row.Entries!);
        Assert.Equal(SharedComponentRelation.Diverged, entry.Relation);
    }

    private static void AddInline(Section section, string tag, string prompt)
    {
        _ = new Block(BlockTypes.AiQuery, section, tag)
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    [Fact]
    public async Task Reconcile_LinkedSectionLiveOrderSwapped_FlagsOrderDeviates()
    {
        Section headDef = DefSection("Risk", ("a", "Prompt A"), ("b", "Prompt B"));
        (string secJson, string secHash) = Definition(headDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        _ = await _harness.AddVersionAsync("c-sec", 1, secHash, secJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-E2" };
        Section section = DefSection("Main", workflow, ("a", "Prompt A"), ("b", "Prompt B"));
        section.Blocks.Single(x => x.ReplacementTag == "a").Order = 1;
        section.Blocks.Single(x => x.ReplacementTag == "b").Order = 0;
        section.SharedComponentId = "c-sec";
        section.SharedComponentLinkState = SharedComponentLinkState.Linked;
        section.SharedComponentVersionHash = secHash;
        section.SharedComponentBindings = new Dictionary<string, string> { ["a"] = "a", ["b"] = "b" };
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-E2"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow row = Assert.Single(result.Value.Instances);
        Assert.True(row.OrderDeviates);
        Assert.NotNull(row.Entries);
        Assert.Equal("a", row.Entries[0].EntryKey);
        Assert.Equal("b", row.Entries[1].EntryKey);
    }

    [Fact]
    public async Task Reconcile_BrokenOutSection_CarriesNoEntryRows()
    {
        Section headDef = DefSection("Risk", ("a", "Prompt A"));
        (string secJson, string secHash) = Definition(headDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        _ = await _harness.AddVersionAsync("c-sec", 1, secHash, secJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-E3" };
        Section section = DefSection("Main", workflow, ("a", "Prompt A"));
        section.SharedComponentId = "c-sec";
        section.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
        section.SharedComponentVersionHash = secHash;
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-E3"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow row = Assert.Single(result.Value.Instances);
        Assert.Null(row.Entries);
        Assert.False(row.OrderDeviates);
    }

    [Fact]
    public async Task Reconcile_BrokenOutSectionWithOverrides_CancelsDeviationsAndOffersRelink()
    {
        Section headDef = DefSection("Risk", ("a", "Prompt A"), ("b", "Prompt B"));
        (string secJson, string secHash) = Definition(headDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: "c-sec");
        _ = await _harness.AddVersionAsync("c-sec", 1, secHash, secJson);

        var workflow = new Workflow("Report") { WorkflowTypeId = "WT-E9" };
        Section section = DefSection("Risk", workflow, ("a", "Prompt A"));
        section.SharedComponentId = "c-sec";
        section.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
        section.SharedComponentVersionHash = secHash;
        section.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["b"] = SharedComponentSlotState.Hidden,
        };
        await _harness.AddWorkflowAsync(workflow);

        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-E9"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentInstanceReconcileRow row = Assert.Single(result.Value.Instances);
        Assert.Equal(SharedComponentRelation.InSync, row.Relation);
        Assert.Equal(
            [SharedComponentReconcileAction.Relink, SharedComponentReconcileAction.KeepLocal],
            row.AvailableActions);
        Assert.Null(row.Entries);
    }

    [Fact]
    public async Task Reconcile_UnknownWorkflowType_ReturnsNotFound()
    {
        ErrorOr<WorkflowSharedComponentReconcileResult> result = await _handler.Handle(
            new GetWorkflowSharedComponentReconcileQuery(ORG, USER, "WT-MISSING"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("WorkflowTemplate.NotFound", result.FirstError.Code);
    }

    private (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        return _harness.Definition(section);
    }

    private (string DefinitionJson, string ContentHash) Definition(Block block)
    {
        return _harness.Definition(block);
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
