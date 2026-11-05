using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.ApplySharedComponentReconcile;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using static IniaAi.Application.UnitTests.Common.Workflows.SharedComponentLinkTestData;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class SharedComponentFanOutServiceApplyTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-apply";
    private const string COMPONENT_ID = "component-1";
    private const string ANCHOR = "WT-ANCHOR";
    private const string SIBLING = "WT-B";

    private static readonly (string Tag, string Prompt)[] _baseBlocks =
        [("rate", "Extract the FX rate"), ("source", "Cite the source")];

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly CoreDbContext _coreContext;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowSavePipeline _pipeline;
    private readonly SharedComponentFanOutService _service;
    private readonly SaveWorkflowCommandHandler _saveHandler;

    public SharedComponentFanOutServiceApplyTests()
    {
        _coreContext = _harness.Context;
        _coreUnitOfWork = _harness.UnitOfWork;
        _pipeline = _harness.CreatePipeline();
        _service = _harness.CreateFanOutService(_pipeline);
        (_, _saveHandler) = _harness.CreateRealSaveSender();
    }

    [Fact]
    public async Task Apply_BreakOut_FlipsLinkStateKeepsProvenance_LinkOnlySave()
    {
        (SharedComponentVersion head, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        string baseContentHash = await VersionContentHashAsync(baseVersionId);
        Section before = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.BreakOut)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.BrokenOut, reloaded.SharedComponentLinkState);
        Assert.Equal(COMPONENT_ID, reloaded.SharedComponentId);
        Assert.Equal(hash, reloaded.SharedComponentVersionHash);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.Equal(ANCHOR, save.WorkflowTypeId);
        Assert.False(save.WasDeduped);
        Assert.NotEqual(baseVersionId, save.VersionId);
        Assert.Equal(baseContentHash, save.ContentHash);
        SharedComponentReconcileDecisionOutcome outcome = Assert.Single(result.Value.DecisionOutcomes);
        Assert.Equal(SharedComponentReconcileAction.BreakOut, outcome.Action);
        Assert.False(outcome.AlreadyCurrent);
        SharedComponentReconciledHead componentHead = Assert.Single(result.Value.ComponentHeads);
        Assert.Equal(head.Id, componentHead.HeadVersionId);
        Assert.False(componentHead.Appended);
    }

    [Fact]
    public async Task Apply_KeepLocal_DedupsAnchorSaveOnly()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.True(save.WasDeduped);
        Assert.Equal(baseVersionId, save.VersionId);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
    }

    [Fact]
    public async Task Apply_AdoptHead_GraftsHeadOntoLaggingInstance()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        (string jsonV2, string hashV2) = Definition(
            DefSection("Market Data", ("rate", "Extract the spot FX rate"), ("source", "Cite the source")));
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hashV1, _baseBlocks));
        Section before = await LinkedSectionAsync(ANCHOR);
        Dictionary<string, string> idsBefore = before.Blocks.ToDictionary(
            b => b.ReplacementTag, b => b.StableBlockId!, StringComparer.Ordinal);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v2.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Equal(hashV2, reloaded.SharedComponentVersionHash);
        Block rate = reloaded.Blocks.Single(b => b.ReplacementTag == "rate");
        Assert.Equal("Extract the spot FX rate", ((AiQueryProperties)rate.Properties).UserPromptTemplate);
        Assert.Equal(idsBefore["rate"], rate.StableBlockId);
        Assert.Equal(idsBefore["source"], reloaded.Blocks.Single(b => b.ReplacementTag == "source").StableBlockId);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SequenceNumber == 2, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_AdoptHead_OnBrokenOutFork_AlsoRelinks()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1,
            [("rate", "Locally forked"), ("source", "Cite the source")],
            linkState: SharedComponentLinkState.BrokenOut));
        Section before = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Equal(hashV1, reloaded.SharedComponentVersionHash);
        Block rate = reloaded.Blocks.Single(b => b.ReplacementTag == "rate");
        Assert.Equal("Extract the FX rate", ((AiQueryProperties)rate.Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_Relink_CleanBlockFork_RelinksByTargetPathWithoutRewrite()
    {
        (SharedComponentVersion head, string hash) = await SeedBlockComponentAsync("Who signs?");
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, BlockConsumer(
            hash, "Who signs?", SharedComponentLinkState.BrokenOut));

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [new SharedComponentReconcileDecision(
                    COMPONENT_ID,
                    TargetStableId: null,
                    TargetPath: "Sections[1].Blocks[0]",
                    SharedComponentReconcileAction.Relink,
                    head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Block reloaded = await LinkedBlockAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Equal(hash, reloaded.SharedComponentVersionHash);
        Assert.Equal("Who signs?", ((AiQueryProperties)reloaded.Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_Relink_DriftedFork_ConflictsWithContentMismatch()
    {
        (SharedComponentVersion head, string hash) = await SeedBlockComponentAsync("Who signs?");
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, BlockConsumer(
            hash, "Who countersigns?", SharedComponentLinkState.BrokenOut));
        Block before = await LinkedBlockAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableBlockId!, SharedComponentReconcileAction.Relink, head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.CONTENT_MISMATCH, result.FirstError.Code);
        Assert.Equal(hash, result.FirstError.Metadata!["expectedHash"]);
        Assert.NotEqual(hash, result.FirstError.Metadata["actualHash"]);
        Block reloaded = await LinkedBlockAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.BrokenOut, reloaded.SharedComponentLinkState);
    }

    [Fact]
    public async Task Apply_Relink_CleanForkOfAVersionStoredBeforeAFieldWasDropped_Relinks()
    {
        (SharedComponentVersion head, string hash) = await SeedBlockComponentAsync(
            "Who signs?", storedBeforeAFieldWasDropped: true);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, BlockConsumer(
            hash, "Who signs?", SharedComponentLinkState.BrokenOut));
        Block before = await LinkedBlockAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableBlockId!, SharedComponentReconcileAction.Relink, head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Block reloaded = await LinkedBlockAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Equal(hash, reloaded.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Apply_Relink_AlreadyLinkedNode_NoOpKeepsStaleLinkHash()
    {
        (SharedComponentVersion head, _) = await SeedBlockComponentAsync("Who signs?");
        string staleHash = new('d', 64);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, BlockConsumer(
            staleHash, "Who countersigns?", SharedComponentLinkState.Linked));
        Block before = await LinkedBlockAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableBlockId!, SharedComponentReconcileAction.Relink, head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Block reloaded = await LinkedBlockAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Equal(staleHash, reloaded.SharedComponentVersionHash);
        Assert.Equal("Who countersigns?", ((AiQueryProperties)reloaded.Properties).UserPromptTemplate);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.True(save.WasDeduped);
        Assert.Equal(baseVersionId, save.VersionId);
    }

    [Fact]
    public async Task Apply_Relink_SectionWithHiddenEntry_ClearsTombstoneAndRestoresEntry()
    {
        (SharedComponentVersion head, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks, linkState: SharedComponentLinkState.BrokenOut);
        Section seeded = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = seeded.Blocks.Remove(seeded.Blocks.Single(b => b.ReplacementTag == "rate"));
        seeded.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.Hidden,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section before = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.Relink, head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Equal(hash, reloaded.SharedComponentVersionHash);
        Assert.Null(reloaded.SharedComponentSlotStates);
        List<Block> ordered = [.. reloaded.Blocks.OrderBy(b => b.Order)];
        Assert.Equal(["rate", "source"], ordered.Select(b => b.ReplacementTag).ToArray());
        Assert.Equal("Extract the FX rate", ((AiQueryProperties)ordered[0].Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_Relink_SectionWithPrivateExtraBlock_RelinksAndKeepsExtra()
    {
        (SharedComponentVersion head, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks, linkState: SharedComponentLinkState.BrokenOut);
        Section seeded = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = new Block(BlockTypes.AiQuery, seeded, seeded.Blocks.Count, "custom")
        {
            Name = "Local extra",
            Properties = new AiQueryProperties { UserPromptTemplate = "Custom note" },
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section before = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.Relink, head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Equal(hash, reloaded.SharedComponentVersionHash);
        Assert.Contains(reloaded.Blocks, b => b.ReplacementTag == "custom");
    }

    [Fact]
    public async Task Apply_Relink_SectionWithBrokenOutEntry_GraftsDefinitionKeepingStableId()
    {
        (SharedComponentVersion head, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(
            hash,
            [("rate", "Locally improved"), ("source", "Cite the source")],
            linkState: SharedComponentLinkState.BrokenOut);
        Section seeded = consumer.Sections.Single(s => s.SharedComponentId is not null);
        seeded.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.BrokenOut,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section before = await LinkedSectionAsync(ANCHOR);
        string forkStableId = before.Blocks.Single(b => b.ReplacementTag == "rate").StableBlockId!;

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.Relink, head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Null(reloaded.SharedComponentSlotStates);
        Block rate = reloaded.Blocks.Single(b => b.ReplacementTag == "rate");
        Assert.Equal("Extract the FX rate", ((AiQueryProperties)rate.Properties).UserPromptTemplate);
        Assert.Equal(forkStableId, rate.StableBlockId);
    }

    [Fact]
    public async Task Apply_Relink_SectionWithHiddenRefEntry_RehydratesInnerLink()
    {
        (SharedComponentVersion secHead, _, string kycHash, string secHash) = await SeedRefEntrySectionComponentAsync();
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: false);
        Section seeded = consumer.Sections.Single(s => s.SharedComponentId is not null);
        seeded.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
        seeded.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["kyc"] = SharedComponentSlotState.Hidden,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section before = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.Relink, secHead.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Null(reloaded.SharedComponentSlotStates);
        Block inner = reloaded.Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Equal("c-kyc", inner.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, inner.SharedComponentLinkState);
        Assert.Equal(kycHash, inner.SharedComponentVersionHash);
        Assert.Equal("KYC v1", ((AiQueryProperties)inner.Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_Relink_SectionWithBrokenOutRefEntry_ReconnectsInnerToCurrentHead()
    {
        (SharedComponentVersion secHead, _, string kycHash, string secHash) = await SeedRefEntrySectionComponentAsync();
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: true);
        Section seeded = consumer.Sections.Single(s => s.SharedComponentId is not null);
        seeded.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
        Block seededInner = seeded.Blocks.Single(b => b.ReplacementTag == "kyc");
        seededInner.SharedComponentLinkState = SharedComponentLinkState.BrokenOut;
        ((AiQueryProperties)seededInner.Properties).UserPromptTemplate = "KYC local fork";
        seeded.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["kyc"] = SharedComponentSlotState.BrokenOut,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section before = await LinkedSectionAsync(ANCHOR);
        string innerStableId = before.Blocks.Single(b => b.ReplacementTag == "kyc").StableBlockId!;

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.Relink, secHead.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.Null(reloaded.SharedComponentSlotStates);
        Block inner = reloaded.Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Equal(SharedComponentLinkState.Linked, inner.SharedComponentLinkState);
        Assert.Equal(kycHash, inner.SharedComponentVersionHash);
        Assert.Equal("KYC v1", ((AiQueryProperties)inner.Properties).UserPromptTemplate);
        Assert.Equal(innerStableId, inner.StableBlockId);
    }

    [Fact]
    public async Task Apply_UpdateShared_HiddenSlot_PushesCurationRemovingTheEntry()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hashV1, [("rate", "Extract the FX rate"), ("source", "Cite the source")]);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = linked.Blocks.Remove(linked.Blocks.Single(b => b.ReplacementTag == "rate"));
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.Hidden,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section anchorBefore = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(anchorBefore.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentReconciledHead newHead = Assert.Single(result.Value.ComponentHeads);
        Assert.True(newHead.Appended);
        Assert.Equal(2, await _coreContext.SharedComponentVersions.AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken));
        SharedComponentVersion headRow = await _coreContext.SharedComponentVersions.AsNoTracking()
            .SingleAsync(v => v.Id == newHead.HeadVersionId, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("rate", headRow.ContentJson);
        Assert.Contains("source", headRow.ContentJson);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Null(reloaded.SharedComponentSlotStates);
        Assert.Equal("source", Assert.Single(reloaded.Blocks).ReplacementTag);
    }

    [Fact]
    public async Task Apply_UpdateShared_AppendsDerivedDefinitionAndFansOut()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1, [("rate", "Locally improved"), ("source", "Cite the source")]));
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", SIBLING, LinkedConsumer(hashV1, _baseBlocks));
        Section anchorBefore = await LinkedSectionAsync(ANCHOR);
        Section siblingBefore = await LinkedSectionAsync(SIBLING);
        Dictionary<string, string> siblingIds = siblingBefore.Blocks.ToDictionary(
            b => b.ReplacementTag, b => b.StableBlockId!, StringComparer.Ordinal);
        (string expectedJsonV2, string expectedHashV2) = Definition(
            DefSection("Market Data", ("rate", "Locally improved"), ("source", "Cite the source")));

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(anchorBefore.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentVersion v2 = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(v => v.SequenceNumber == 2, TestContext.Current.CancellationToken);
        Assert.Equal(v1.Id, v2.ParentVersionId);
        Assert.Equal(USER, v2.CreatedByUserId);
        Assert.Equal(expectedHashV2, v2.ContentHash);
        Assert.Equal(expectedJsonV2, v2.ContentJson);

        SharedComponentReconciledHead componentHead = Assert.Single(result.Value.ComponentHeads);
        Assert.Equal(v2.Id, componentHead.HeadVersionId);
        Assert.Equal(expectedHashV2, componentHead.HeadContentHash);
        Assert.True(componentHead.Appended);

        Section anchorAfter = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(expectedHashV2, anchorAfter.SharedComponentVersionHash);
        Assert.Equal(
            "Locally improved",
            ((AiQueryProperties)anchorAfter.Blocks.Single(b => b.ReplacementTag == "rate").Properties).UserPromptTemplate);

        Section siblingAfter = await LinkedSectionAsync(SIBLING);
        Assert.Equal(expectedHashV2, siblingAfter.SharedComponentVersionHash);
        Block siblingRate = siblingAfter.Blocks.Single(b => b.ReplacementTag == "rate");
        Assert.Equal("Locally improved", ((AiQueryProperties)siblingRate.Properties).UserPromptTemplate);
        Assert.Equal(siblingIds["rate"], siblingRate.StableBlockId);
        Assert.Equal(siblingIds["source"], siblingAfter.Blocks.Single(b => b.ReplacementTag == "source").StableBlockId);

        Assert.Equal(2, result.Value.WorkflowSaves.Count);
        SharedComponentConsumerSaveResult siblingSave = result.Value.WorkflowSaves.Single(s => s.WorkflowTypeId == SIBLING);
        WorkflowVersion siblingHead = await _coreContext.WorkflowVersions
            .AsNoTracking()
            .SingleAsync(v => v.Id == siblingSave.VersionId, TestContext.Current.CancellationToken);
        Assert.Equal(
            $"Updated from shared component 'Market Data' ({expectedHashV2[..8]})",
            siblingHead.AutoNarrativeSummary);
        SharedComponentReconcileDecisionOutcome outcome = Assert.Single(result.Value.DecisionOutcomes);
        Assert.False(outcome.AlreadyCurrent);
    }

    [Fact]
    public async Task Apply_UpdateShared_BindingsInstance_RestoresSlotTagsInStoredDefinition()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1,
            [("rate_2", "Locally improved"), ("source", "Cite the source")],
            bindings: new Dictionary<string, string> { ["rate"] = "rate_2" }));
        Section before = await LinkedSectionAsync(ANCHOR);
        (string expectedJsonV2, string expectedHashV2) = Definition(
            DefSection("Market Data", ("rate", "Locally improved"), ("source", "Cite the source")));

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentVersion v2 = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(v => v.SequenceNumber == 2, TestContext.Current.CancellationToken);
        Assert.Equal(expectedJsonV2, v2.ContentJson);
        Assert.Equal(expectedHashV2, v2.ContentHash);
        Assert.Contains(""""ReplacementTag":"rate"""", v2.ContentJson);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal("rate_2", reloaded.SharedComponentBindings!["rate"]);
        Assert.Equal(expectedHashV2, reloaded.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Apply_UpdateShared_AlreadyAtHead_DedupsAndSkipsFanOut()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hashV1, _baseBlocks));
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", SIBLING, LinkedConsumer(hashV1, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentReconcileDecisionOutcome outcome = Assert.Single(result.Value.DecisionOutcomes);
        Assert.True(outcome.AlreadyCurrent);
        SharedComponentReconciledHead componentHead = Assert.Single(result.Value.ComponentHeads);
        Assert.Equal(v1.Id, componentHead.HeadVersionId);
        Assert.False(componentHead.Appended);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.Equal(ANCHOR, save.WorkflowTypeId);
        Assert.True(save.WasDeduped);
    }

    [Fact]
    public async Task Apply_UpdateShared_StaleExpectedHead_ConflictsWithHeadMoved()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        (string jsonV2, string hashV2) = Definition(
            DefSection("Market Data", ("rate", "Extract the spot FX rate"), ("source", "Cite the source")));
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1, [("rate", "Locally improved"), ("source", "Cite the source")]));
        Section instance = await LinkedSectionAsync(ANCHOR);
        int workflowVersionsBefore = await _coreContext.WorkflowVersions
            .CountAsync(TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.HEAD_MOVED, result.FirstError.Code);
        Assert.Equal(v2.Id, result.FirstError.Metadata!["currentHeadVersionId"]);
        Assert.Equal(2, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
        Assert.Equal(workflowVersionsBefore, await _coreContext.WorkflowVersions
            .CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_StaleBaseVersionId_ConflictsAndAppliesNothing()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1, [("rate", "Locally improved"), ("source", "Cite the source")]));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, "stale-base-version",
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(WorkflowSavePipeline.VERSION_CONFLICT_CODE, result.FirstError.Code);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(
            _coreContext.ChangeTracker.Entries<SharedComponentVersion>(),
            e => e.State == EntityState.Added);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(hashV1, reloaded.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Apply_UpdateSharedPlusKeepLocal_SameComponent_ConflictingDecisions()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow anchor = LinkedConsumer(hashV1, [("rate", "Locally improved"), ("source", "Cite the source")]);
        Section second = DefSection("Market Data", anchor, ("rate_b", "Extract the FX rate"), ("source_b", "Cite the source"));
        second.SharedComponentId = COMPONENT_ID;
        second.SharedComponentLinkState = SharedComponentLinkState.Linked;
        second.SharedComponentVersionHash = hashV1;
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, anchor);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(ANCHOR))!;
        List<Section> instances = [.. reloaded.Sections.Where(s => s.SharedComponentId == COMPONENT_ID)];
        Assert.Equal(2, instances.Count);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [
                    Decision(instances[0].StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id),
                    Decision(instances[1].StableSectionId!, SharedComponentReconcileAction.KeepLocal),
                ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.CONFLICTING_DECISIONS, result.FirstError.Description);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_UpdateShared_RemovedSlotOrphans_RequireAcknowledgementThenDelete()
    {
        (SharedComponentVersion v1, _, string hashV1) =
            await SeedSectionComponentAsync(_baseBlocks, declareSourceSignatureFields: true);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1, [("rate", "Extract the FX rate")]));
        Workflow siblingConsumer = LinkedConsumer(hashV1, _baseBlocks);
        DeclareDirectorSignature(siblingConsumer.Sections.Single(s => s.SharedComponentId == COMPONENT_ID));
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", SIBLING, siblingConsumer);
        Section anchorInstance = await LinkedSectionAsync(ANCHOR);
        Section siblingInstance = await LinkedSectionAsync(SIBLING);
        string siblingSourceStableId = siblingInstance.Blocks.Single(b => b.ReplacementTag == "source").StableBlockId!;
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-client",
            WorkflowTypeId = SIBLING,
            PlaceholderName = "director_name",
            Value = "Jane Director",
        });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentReconcileApplyResult> refused = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(anchorInstance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(ErrorType.Conflict, refused.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT, refused.FirstError.Code);
        var orphans = Assert.IsAssignableFrom<IReadOnlyList<SharedComponentOverrideImpact>>(
            refused.FirstError.Metadata!["orphanedOverrides"]);
        SharedComponentOverrideImpact orphan = Assert.Single(orphans);
        Assert.Equal("org-client", orphan.OrganizationId);
        Assert.Equal(SIBLING, orphan.WorkflowTypeId);
        Assert.Equal("director_name", orphan.PlaceholderName);
        Assert.Equal(SharedComponentOverrideImpactKind.Deleted, orphan.Kind);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));

        ErrorOr<SharedComponentReconcileApplyResult> accepted = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(anchorInstance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id, ack: true)]),
            TestContext.Current.CancellationToken);

        Assert.False(accepted.IsError);
        Section siblingAfter = await LinkedSectionAsync(SIBLING);
        Assert.DoesNotContain(siblingAfter.Blocks, b => b.ReplacementTag == "source");
        SharedComponentConsumerSaveResult siblingSave = accepted.Value.WorkflowSaves
            .Single(s => s.WorkflowTypeId == SIBLING);
        SharedComponentOverrideImpact reported = Assert.Single(siblingSave.OrphanedOverrides);
        Assert.Equal("director_name", reported.PlaceholderName);
        Assert.Equal(0, await _coreContext.SignatureFieldOverrides
            .CountAsync(o => o.WorkflowTypeId == SIBLING, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_AdoptHead_RemovingSlot_RequiresAcknowledgement()
    {
        (SharedComponentVersion v1, _, string hashV1) =
            await SeedSectionComponentAsync(_baseBlocks, declareSourceSignatureFields: true);
        (string jsonV2, string hashV2) = Definition(DefSection("Market Data", ("rate", "Extract the FX rate")));
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hashV1, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);
        string sourceStableId = instance.Blocks.Single(b => b.ReplacementTag == "source").StableBlockId!;
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-client",
            WorkflowTypeId = ANCHOR,
            PlaceholderName = "director_name",
            Value = "Jane Director",
        });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentReconcileApplyResult> refused = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v2.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT, refused.FirstError.Code);

        ErrorOr<SharedComponentReconcileApplyResult> accepted = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v2.Id, ack: true)]),
            TestContext.Current.CancellationToken);

        Assert.False(accepted.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.DoesNotContain(reloaded.Blocks, b => b.ReplacementTag == "source");
        Assert.Equal(hashV2, reloaded.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Apply_OrphanRefusal_CarriesTheOrphansToTheAcknowledgementReader()
    {
        (SharedComponentVersion v1, _, string hashV1) =
            await SeedSectionComponentAsync(_baseBlocks, declareSourceSignatureFields: true);
        (string jsonV2, string hashV2) = Definition(DefSection("Market Data", ("rate", "Extract the FX rate")));
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        string baseVersionId = await _harness.SaveTemplateAsync(
            _saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hashV1, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-client",
            WorkflowTypeId = ANCHOR,
            PlaceholderName = "director_name",
            Value = "Jane Director",
        });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentReconcileApplyResult> refused = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v2.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT, refused.FirstError.Code);

        SharedComponentOverrideImpact orphan = Assert.Single(
            SharedComponentOrphanMetadata.ReadOrphanedOverrides(refused.FirstError));
        Assert.Equal("org-client", orphan.OrganizationId);
        Assert.Equal(ANCHOR, orphan.WorkflowTypeId);
        Assert.Equal("director_name", orphan.PlaceholderName);
    }

    [Fact]
    public async Task Apply_AdoptHead_BlockPlaceholderDropped_RequiresAcknowledgement()
    {
        Block definitionV1 = BuildBlockDefinition("Who signs?");
        ((AiQueryProperties)definitionV1.Properties).SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["director_name"] = new("Director", "Signing director", "the director"),
        };
        (string jsonV1, string hashV1) = Definition(definitionV1);
        Assert.Contains("SignatureFields", jsonV1);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer Check", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, jsonV1);
        (string jsonV2, string hashV2) = Definition(BuildBlockDefinition("Who signs?"));
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        Workflow consumer = BlockConsumer(hashV1, "Who signs?", SharedComponentLinkState.Linked);
        Block consumerBlock = consumer.GetAllBlocks().Single(b => b.SharedComponentId == COMPONENT_ID);
        ((AiQueryProperties)consumerBlock.Properties).SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["director_name"] = new("Director", "Signing director", "the director"),
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Block instance = await LinkedBlockAsync(ANCHOR);
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-client",
            WorkflowTypeId = ANCHOR,
            PlaceholderName = "director_name",
            Value = "Jane Director",
        });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentReconcileApplyResult> refused = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableBlockId!, SharedComponentReconcileAction.AdoptHead, v2.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT, refused.FirstError.Code);

        ErrorOr<SharedComponentReconcileApplyResult> accepted = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableBlockId!, SharedComponentReconcileAction.AdoptHead, v2.Id, ack: true)]),
            TestContext.Current.CancellationToken);

        Assert.False(accepted.IsError);
        SharedComponentConsumerSaveResult save = Assert.Single(accepted.Value.WorkflowSaves);
        SharedComponentOverrideImpact reported = Assert.Single(save.OrphanedOverrides);
        Assert.Equal(SharedComponentOverrideImpactKind.GoingInert, reported.Kind);
        Assert.Equal("director_name", reported.PlaceholderName);
        Block reloaded = await LinkedBlockAsync(ANCHOR);
        Assert.Equal(hashV2, reloaded.SharedComponentVersionHash);
        Assert.Null(((AiQueryProperties)reloaded.Properties).SignatureFields);
        Assert.Equal(1, await _coreContext.SignatureFieldOverrides
            .CountAsync(o => o.PlaceholderName == "director_name", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_AnchorSaveThrows_DetachesPendingVersions()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1, [("rate", "Locally improved"), ("source", "Cite the source")]));
        Section instance = await LinkedSectionAsync(ANCHOR);
        SharedComponentFanOutService throwingService = _harness.CreateFanOutService(
            new ThrowingWorkflowSavePipeline(_harness, () => new InvalidOperationException("boom")));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => throwingService.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken));

        Assert.DoesNotContain(
            _coreContext.ChangeTracker.Entries<SharedComponentVersion>(),
            e => e.State == EntityState.Added);
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_EchoMismatchOrPlainNode_RejectsLocator()
    {
        (_, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Other", id: "component-2");
        _ = await _harness.AddVersionAsync("component-2", 1, new string('d', 64), """{"Title":"Other"}""");
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hashV1, _baseBlocks));
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(ANCHOR))!;
        Section linked = reloaded.Sections.Single(s => s.SharedComponentId == COMPONENT_ID);
        Section intro = reloaded.Sections.Single(s => s.Title == "Intro");

        ErrorOr<SharedComponentReconcileApplyResult> mismatch = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(linked.StableSectionId!, SharedComponentReconcileAction.BreakOut, componentId: "component-2")]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(mismatch);

        ErrorOr<SharedComponentReconcileApplyResult> plain = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(intro.StableSectionId!, SharedComponentReconcileAction.BreakOut)]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(plain);
    }

    [Fact]
    public async Task Apply_RetiredComponent_RefusesUpdateShared_AllowsBreakOut()
    {
        (string json, string hash) = Definition(DefSection("Market Data", _baseBlocks));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", SharedComponentState.Retired, id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, json);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> refused = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(SharedComponentErrorCodes.RETIRED, refused.FirstError.Code);

        ErrorOr<SharedComponentReconcileApplyResult> breakOut = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.BreakOut)]),
            TestContext.Current.CancellationToken);

        Assert.False(breakOut.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.BrokenOut, reloaded.SharedComponentLinkState);
    }

    [Fact]
    public async Task Apply_EntryDecision_BothLocators_RejectsLocator()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);
        string rateStableId = instance.Blocks.Single(b => b.ReplacementTag == "rate").StableBlockId!;

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal,
                    entryKey: "rate", entryStableId: rateStableId)]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(result);
    }

    [Fact]
    public async Task Apply_EntryDecision_OnBlockCopy_RejectsLocator()
    {
        (_, string hash) = await SeedBlockComponentAsync("Who signs?");
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, BlockConsumer(
            hash, "Who signs?", SharedComponentLinkState.Linked));
        Block instance = await LinkedBlockAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableBlockId!, SharedComponentReconcileAction.KeepLocal, entryKey: "signer")]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(result);
    }

    [Fact]
    public async Task Apply_EntryDecision_OnBrokenOutSection_RejectsLocator()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hash, _baseBlocks, linkState: SharedComponentLinkState.BrokenOut));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(result);
    }

    [Fact]
    public async Task Apply_EntryStableId_UnknownInSection_TargetNotFound()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal,
                    entryStableId: "no-such-block")]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.TARGET_NOT_FOUND, result.FirstError.Code);
    }

    [Theory]
    [InlineData(SharedComponentReconcileAction.Hide)]
    [InlineData(SharedComponentReconcileAction.Unhide)]
    public async Task Apply_HideFamilyWithoutEntryLocator_RejectsLocator(SharedComponentReconcileAction action)
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, action)]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(result);
    }

    [Fact]
    public async Task Apply_EntryRelink_RejectsLocator()
    {
        (SharedComponentVersion head, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Relink, head.Id, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(result);
    }

    [Fact]
    public async Task Apply_TwoEntryDecisions_DifferentEntries_BothAccepted()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal, entryKey: "rate"),
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal, entryKey: "source"),
                ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.DecisionOutcomes.Count);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.True(save.WasDeduped);
    }

    [Fact]
    public async Task Apply_TwoDecisions_SameEntryByKeyAndBoundStableId_ConflictingDecisions()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hash,
            [("rate_2", "Extract the FX rate"), ("source", "Cite the source")],
            bindings: new Dictionary<string, string> { ["rate"] = "rate_2" }));
        Section instance = await LinkedSectionAsync(ANCHOR);
        string renamedStableId = instance.Blocks.Single(b => b.ReplacementTag == "rate_2").StableBlockId!;

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal, entryKey: "rate"),
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal, entryStableId: renamedStableId),
                ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.CONFLICTING_DECISIONS, result.FirstError.Description);
    }

    [Fact]
    public async Task Apply_WholeSectionBreakOutPlusEntryDecision_ConflictingDecisions()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.BreakOut),
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal, entryKey: "rate"),
                ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.CONFLICTING_DECISIONS, result.FirstError.Description);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
    }

    [Fact]
    public async Task Apply_WholeSectionKeepLocalPlusEntryKeepLocal_BothNoOp()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal),
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.KeepLocal, entryKey: "rate"),
                ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.DecisionOutcomes.Count);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.True(save.WasDeduped);
        Assert.Equal(baseVersionId, save.VersionId);
    }

    [Fact]
    public async Task Apply_Hide_DefEntry_RemovesBlockAndRecordsHiddenSlot()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Hide, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal("source", Assert.Single(reloaded.Blocks).ReplacementTag);
        Assert.Equal(SharedComponentSlotState.Hidden, reloaded.SharedComponentSlotStates!["rate"]);
        Assert.Null(reloaded.SharedComponentBindings);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.False(save.WasDeduped);
        SharedComponentReconciledHead head = Assert.Single(result.Value.ComponentHeads);
        Assert.False(head.Appended);
    }

    [Fact]
    public async Task Apply_Hide_RenamedEntry_RemovesBoundBlockAndDropsBinding()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hash,
            [("rate_2", "Extract the FX rate"), ("source", "Cite the source")],
            bindings: new Dictionary<string, string> { ["rate"] = "rate_2" }));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Hide, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal("source", Assert.Single(reloaded.Blocks).ReplacementTag);
        Assert.Equal(SharedComponentSlotState.Hidden, reloaded.SharedComponentSlotStates!["rate"]);
        Assert.Null(reloaded.SharedComponentBindings);
    }

    [Fact]
    public async Task Apply_Hide_AlreadyHidden_NoOp()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = linked.Blocks.Remove(linked.Blocks.Single(b => b.ReplacementTag == "rate"));
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.Hidden,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Hide, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.True(save.WasDeduped);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentSlotState.Hidden, reloaded.SharedComponentSlotStates!["rate"]);
    }

    [Fact]
    public async Task Apply_Hide_LocalInsertEntry_RejectsLocator()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = new Block(BlockTypes.AiQuery, linked, "local_note")
        {
            Name = "Note",
            Properties = new AiQueryProperties { UserPromptTemplate = "Local note" },
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);
        string insertStableId = instance.Blocks.Single(b => b.ReplacementTag == "local_note").StableBlockId!;

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Hide, entryStableId: insertStableId)]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(result);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Contains(reloaded.Blocks, b => b.ReplacementTag == "local_note");
    }

    [Fact]
    public async Task Apply_Unhide_HiddenEntry_RematerializesAtDefPosition()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = linked.Blocks.Remove(linked.Blocks.Single(b => b.ReplacementTag == "rate"));
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.Hidden,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Unhide, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(["rate", "source"], reloaded.Blocks.OrderBy(b => b.Order).Select(b => b.ReplacementTag).ToList());
        Block rate = reloaded.Blocks.Single(b => b.ReplacementTag == "rate");
        Assert.Equal("Extract the FX rate", ((AiQueryProperties)rate.Properties).UserPromptTemplate);
        Assert.False(string.IsNullOrEmpty(rate.StableBlockId));
        Assert.Null(reloaded.SharedComponentSlotStates);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.False(save.WasDeduped);
        SharedComponentReconciledHead head = Assert.Single(result.Value.ComponentHeads);
        Assert.False(head.Appended);
    }

    [Fact]
    public async Task Apply_Unhide_NotHidden_NoOp()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Unhide, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.True(save.WasDeduped);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(2, reloaded.Blocks.Count);
    }

    [Fact]
    public async Task Apply_Unhide_SlotDeletedFromHead_DropsTombstoneOnly()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["ghost"] = SharedComponentSlotState.Hidden,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Unhide, entryKey: "ghost")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Null(reloaded.SharedComponentSlotStates);
        Assert.Equal(2, reloaded.Blocks.Count);
    }

    [Fact]
    public async Task Apply_Unhide_TagTaken_LandsUniqueTagWithBinding()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = linked.Blocks.Remove(linked.Blocks.Single(b => b.ReplacementTag == "rate"));
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.Hidden,
        };
        _ = new Block(BlockTypes.AiQuery, linked, "rate")
        {
            Name = "Squatter",
            Properties = new AiQueryProperties { UserPromptTemplate = "Local rate note" },
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Unhide, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Block landed = reloaded.Blocks.Single(b =>
            ((AiQueryProperties)b.Properties).UserPromptTemplate == "Extract the FX rate");
        Assert.NotEqual("rate", landed.ReplacementTag);
        Assert.Equal(landed.ReplacementTag, reloaded.SharedComponentBindings!["rate"]);
        Assert.Contains(reloaded.Blocks, b => b.ReplacementTag == "rate" && b.Name == "Squatter");
        Assert.Null(reloaded.SharedComponentSlotStates);
    }

    [Fact]
    public async Task Apply_EntryBreakOut_InlineEntry_RecordsBrokenOutSlot()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hash, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.BreakOut, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentSlotState.BrokenOut, reloaded.SharedComponentSlotStates!["rate"]);
        Assert.Equal(2, reloaded.Blocks.Count);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.False(save.WasDeduped);
    }

    [Fact]
    public async Task Apply_EntryBreakOut_AlreadyBrokenOut_NoOp()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.BrokenOut,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.BreakOut, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.True(save.WasDeduped);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(SharedComponentSlotState.BrokenOut, reloaded.SharedComponentSlotStates!["rate"]);
    }

    [Fact]
    public async Task Apply_EntryBreakOut_HiddenEntry_RejectsLocator()
    {
        (_, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = linked.Blocks.Remove(linked.Blocks.Single(b => b.ReplacementTag == "rate"));
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.Hidden,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.BreakOut, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(result);
    }

    [Fact]
    public async Task Apply_EntryBreakOut_RefEntry_FlipsInnerLinkStateThenNoOps()
    {
        (_, _, string kycHash, string secHash) = await SeedRefEntrySectionComponentAsync();
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: true);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.BreakOut, entryKey: "kyc")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Block inner = reloaded.Blocks.Single(b => b.SharedComponentId == "c-kyc");
        Assert.Equal(SharedComponentLinkState.BrokenOut, inner.SharedComponentLinkState);
        Assert.Equal(kycHash, inner.SharedComponentVersionHash);
        Assert.Null(reloaded.SharedComponentSlotStates);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);

        string nextBaseVersionId = Assert.Single(result.Value.WorkflowSaves).VersionId;
        ErrorOr<SharedComponentReconcileApplyResult> again = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, nextBaseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.BreakOut, entryKey: "kyc")]),
            TestContext.Current.CancellationToken);

        Assert.False(again.IsError);
        Assert.True(Assert.Single(again.Value.WorkflowSaves).WasDeduped);
    }

    [Fact]
    public async Task Apply_Unhide_RefEntry_HydratesQuartetFromRefHead()
    {
        (_, _, string kycHash, string secHash) = await SeedRefEntrySectionComponentAsync();
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: false);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["kyc"] = SharedComponentSlotState.Hidden,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.Unhide, entryKey: "kyc")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(["rate", "kyc"], reloaded.Blocks.OrderBy(b => b.Order).Select(b => b.ReplacementTag).ToList());
        Block inner = reloaded.Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Equal("c-kyc", inner.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.Linked, inner.SharedComponentLinkState);
        Assert.Equal(kycHash, inner.SharedComponentVersionHash);
        Assert.Equal("KYC v1", ((AiQueryProperties)inner.Properties).UserPromptTemplate);
        Assert.False(string.IsNullOrEmpty(inner.StableBlockId));
        Assert.Null(reloaded.SharedComponentSlotStates);
    }

    [Fact]
    public async Task Apply_EntryAdoptHead_InlineHeadAhead_GraftsOnlyThatEntry()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        (string jsonV2, string hashV2) = Definition(
            DefSection("Market Data", ("rate", "Extract the spot FX rate"), ("source", "Cite two sources")));
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hashV1, _baseBlocks));
        Section before = await LinkedSectionAsync(ANCHOR);
        string rateStableId = before.Blocks.Single(b => b.ReplacementTag == "rate").StableBlockId!;

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v2.Id, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Block rate = reloaded.Blocks.Single(b => b.ReplacementTag == "rate");
        Assert.Equal("Extract the spot FX rate", ((AiQueryProperties)rate.Properties).UserPromptTemplate);
        Assert.Equal(rateStableId, rate.StableBlockId);
        Assert.Equal(
            "Cite the source",
            ((AiQueryProperties)reloaded.Blocks.Single(b => b.ReplacementTag == "source").Properties).UserPromptTemplate);
        Assert.Equal(hashV1, reloaded.SharedComponentVersionHash);
        Assert.Null(reloaded.SharedComponentSlotStates);
    }

    [Fact]
    public async Task Apply_EntryAdoptHead_StaleSectionToken_HeadMoved()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        (string jsonV2, string hashV2) = Definition(
            DefSection("Market Data", ("rate", "Extract the spot FX rate"), ("source", "Cite the source")));
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(hashV1, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v1.Id, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.HEAD_MOVED, result.FirstError.Code);
        Assert.Equal(v2.Id, result.FirstError.Metadata!["currentHeadVersionId"]);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(
            "Extract the FX rate",
            ((AiQueryProperties)reloaded.Blocks.Single(b => b.ReplacementTag == "rate").Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_EntryAdoptHead_MissingEntry_Rematerializes()
    {
        (SharedComponentVersion v1, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = linked.Blocks.Remove(linked.Blocks.Single(b => b.ReplacementTag == "rate"));
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v1.Id, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(["rate", "source"], reloaded.Blocks.OrderBy(b => b.Order).Select(b => b.ReplacementTag).ToList());
        Assert.Equal(
            "Extract the FX rate",
            ((AiQueryProperties)reloaded.Blocks.Single(b => b.ReplacementTag == "rate").Properties).UserPromptTemplate);
        Assert.Null(reloaded.SharedComponentSlotStates);
    }

    [Fact]
    public async Task Apply_EntryAdoptHead_BrokenOutInline_GraftsAndClearsMarker()
    {
        (SharedComponentVersion v1, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, [("rate", "Locally forked"), ("source", "Cite the source")]);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.BrokenOut,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);
        string rateStableId = instance.Blocks.Single(b => b.ReplacementTag == "rate").StableBlockId!;

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v1.Id, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Block rate = reloaded.Blocks.Single(b => b.ReplacementTag == "rate");
        Assert.Equal("Extract the FX rate", ((AiQueryProperties)rate.Properties).UserPromptTemplate);
        Assert.Equal(rateStableId, rate.StableBlockId);
        Assert.Null(reloaded.SharedComponentSlotStates);
    }

    [Fact]
    public async Task Apply_EntryAdoptHead_HiddenEntry_RejectsLocator()
    {
        (SharedComponentVersion v1, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = linked.Blocks.Remove(linked.Blocks.Single(b => b.ReplacementTag == "rate"));
        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = SharedComponentSlotState.Hidden,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v1.Id, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(result);
    }

    [Fact]
    public async Task Apply_EntryAdoptHead_RefEntry_GraftsRefHeadOntoInner()
    {
        (SharedComponentVersion secHead, SharedComponentVersion kycV1, string kycHash, string secHash) =
            await SeedRefEntrySectionComponentAsync();
        (string kycV2Json, string kycV2Hash) = Definition(BuildBlockDefinition("KYC v2"));
        SharedComponentVersion kycV2 = await _harness.AddVersionAsync(
            "c-kyc", 2, kycV2Hash, kycV2Json, parentVersionId: kycV1.Id);
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: true);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);
        string innerStableId = instance.Blocks.Single(b => b.SharedComponentId == "c-kyc").StableBlockId!;

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, secHead.Id,
                    entryKey: "kyc", expectedRefHead: kycV2.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Block inner = reloaded.Blocks.Single(b => b.SharedComponentId == "c-kyc");
        Assert.Equal("KYC v2", ((AiQueryProperties)inner.Properties).UserPromptTemplate);
        Assert.Equal(kycV2Hash, inner.SharedComponentVersionHash);
        Assert.Equal(SharedComponentLinkState.Linked, inner.SharedComponentLinkState);
        Assert.Equal(innerStableId, inner.StableBlockId);
        Assert.Equal("kyc", inner.ReplacementTag);
    }

    [Fact]
    public async Task Apply_EntryAdoptHead_RefEntry_StaleRefToken_HeadMovedNamesToken()
    {
        (SharedComponentVersion secHead, SharedComponentVersion kycV1, string kycHash, string secHash) =
            await SeedRefEntrySectionComponentAsync();
        (string kycV2Json, string kycV2Hash) = Definition(BuildBlockDefinition("KYC v2"));
        SharedComponentVersion kycV2 = await _harness.AddVersionAsync(
            "c-kyc", 2, kycV2Hash, kycV2Json, parentVersionId: kycV1.Id);
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: true);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, secHead.Id,
                    entryKey: "kyc", expectedRefHead: kycV1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.HEAD_MOVED, result.FirstError.Code);
        Assert.Equal(kycV2.Id, result.FirstError.Metadata!["currentHeadVersionId"]);
        Assert.Equal("expectedRefHeadVersionId", result.FirstError.Metadata!["token"]);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(
            "KYC v1",
            ((AiQueryProperties)reloaded.Blocks.Single(b => b.SharedComponentId == "c-kyc").Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_EntryUpdateShared_InlineEntry_MergesFromCurrentHeadDef()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        (string jsonV2, string hashV2) = Definition(
            DefSection("Market Data", ("rate", "Extract the FX rate"), ("source", "Cite two sources")));
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1, [("rate", "Locally improved"), ("source", "Cite the source")]));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v2.Id, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentVersion v3 = await _coreContext.SharedComponentVersions.AsNoTracking()
            .SingleAsync(v => v.SequenceNumber == 3, TestContext.Current.CancellationToken);
        Assert.Equal(v2.Id, v3.ParentVersionId);
        Assert.Contains("Locally improved", v3.ContentJson);
        Assert.Contains("Cite two sources", v3.ContentJson);
        Assert.DoesNotContain("Cite the source", v3.ContentJson);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(v3.ContentHash, reloaded.SharedComponentVersionHash);
        Assert.Equal(
            "Cite two sources",
            ((AiQueryProperties)reloaded.Blocks.Single(b => b.ReplacementTag == "source").Properties).UserPromptTemplate);
        SharedComponentReconciledHead head = Assert.Single(result.Value.ComponentHeads);
        Assert.True(head.Appended);
    }

    [Fact]
    public async Task Apply_EntryUpdateShared_TwoEntriesOneSection_SingleAppend()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1, [("rate", "Improved rate"), ("source", "Improved source")]));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id, entryKey: "rate"),
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id, entryKey: "source"),
                ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, await _coreContext.SharedComponentVersions.AsNoTracking()
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
        SharedComponentVersion v2 = await _coreContext.SharedComponentVersions.AsNoTracking()
            .SingleAsync(v => v.SequenceNumber == 2, TestContext.Current.CancellationToken);
        Assert.Contains("Improved rate", v2.ContentJson);
        Assert.Contains("Improved source", v2.ContentJson);
        Assert.Equal(2, result.Value.DecisionOutcomes.Count);
        Assert.All(result.Value.DecisionOutcomes, o => Assert.False(o.AlreadyCurrent));
        SharedComponentReconciledHead head = Assert.Single(result.Value.ComponentHeads);
        Assert.True(head.Appended);
    }

    [Fact]
    public async Task Apply_EntryUpdateShared_RenamedEntry_RestoresSlotTagInHead()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, LinkedConsumer(
            hashV1,
            [("rate_2", "Locally improved"), ("source", "Cite the source")],
            bindings: new Dictionary<string, string> { ["rate"] = "rate_2" }));
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentVersion v2 = await _coreContext.SharedComponentVersions.AsNoTracking()
            .SingleAsync(v => v.SequenceNumber == 2, TestContext.Current.CancellationToken);
        Assert.Contains("Locally improved", v2.ContentJson);
        Assert.DoesNotContain("rate_2", v2.ContentJson);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(
            "Locally improved",
            ((AiQueryProperties)reloaded.Blocks.Single(b => b.ReplacementTag == "rate_2").Properties).UserPromptTemplate);
        Assert.Equal("rate_2", reloaded.SharedComponentBindings!["rate"]);
    }

    [Fact]
    public async Task Apply_EntryUpdateShared_LocalInsert_AppendsToDefinition()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hashV1, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        _ = new Block(BlockTypes.AiQuery, linked, "local_note")
        {
            Name = "Note",
            Properties = new AiQueryProperties { UserPromptTemplate = "Local note" },
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", SIBLING, LinkedConsumer(hashV1, _baseBlocks));
        Section instance = await LinkedSectionAsync(ANCHOR);
        string insertStableId = instance.Blocks.Single(b => b.ReplacementTag == "local_note").StableBlockId!;

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id,
                    entryStableId: insertStableId)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentVersion v2 = await _coreContext.SharedComponentVersions.AsNoTracking()
            .SingleAsync(v => v.SequenceNumber == 2, TestContext.Current.CancellationToken);
        Assert.Contains("local_note", v2.ContentJson);
        Section anchorAfter = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(3, anchorAfter.Blocks.Count);
        Section siblingAfter = await LinkedSectionAsync(SIBLING);
        Assert.Equal(
            "Local note",
            ((AiQueryProperties)siblingAfter.Blocks.Single(b => b.ReplacementTag == "local_note").Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_EntryUpdateShared_RefEntry_PushesRefComponentOwnPlan()
    {
        (SharedComponentVersion secHead, SharedComponentVersion kycV1, string kycHash, string secHash) =
            await SeedRefEntrySectionComponentAsync();
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: true);
        Section linkedBefore = consumer.Sections.Single(s => s.SharedComponentId is not null);
        Block innerBefore = linkedBefore.Blocks.Single(b => b.SharedComponentId == "c-kyc");
        ((AiQueryProperties)innerBefore.Properties).UserPromptTemplate = "KYC improved";
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, secHead.Id,
                    entryKey: "kyc", expectedRefHead: kycV1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, await _coreContext.SharedComponentVersions.AsNoTracking()
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
        SharedComponentVersion kycV2 = await _coreContext.SharedComponentVersions.AsNoTracking()
            .SingleAsync(
                v => v.SharedComponentId == "c-kyc" && v.SequenceNumber == 2,
                TestContext.Current.CancellationToken);
        Assert.Contains("KYC improved", kycV2.ContentJson);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Block inner = reloaded.Blocks.Single(b => b.SharedComponentId == "c-kyc");
        Assert.Equal(kycV2.ContentHash, inner.SharedComponentVersionHash);
        Assert.Equal(SharedComponentLinkState.Linked, inner.SharedComponentLinkState);
        SharedComponentReconciledHead kycHead = result.Value.ComponentHeads.Single(h => h.SharedComponentId == "c-kyc");
        Assert.True(kycHead.Appended);
        SharedComponentReconciledHead sectionHead = result.Value.ComponentHeads.Single(h => h.SharedComponentId == COMPONENT_ID);
        Assert.False(sectionHead.Appended);
    }

    [Fact]
    public async Task Apply_EntryUpdateShared_RefEntry_MissingRefToken_HeadMovedNamesToken()
    {
        (SharedComponentVersion secHead, _, string kycHash, string secHash) = await SeedRefEntrySectionComponentAsync();
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: true);
        Block innerBefore = consumer.Sections.Single(s => s.SharedComponentId is not null)
            .Blocks.Single(b => b.SharedComponentId == "c-kyc");
        ((AiQueryProperties)innerBefore.Properties).UserPromptTemplate = "KYC improved";
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, secHead.Id,
                    entryKey: "kyc")]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.HEAD_MOVED, result.FirstError.Code);
        Assert.Equal("expectedRefHeadVersionId", result.FirstError.Metadata!["token"]);
        Assert.Equal(1, await _coreContext.SharedComponentVersions.AsNoTracking()
            .CountAsync(v => v.SharedComponentId == "c-kyc", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Apply_EntryUpdateShared_HiddenOrBrokenOutEntry_RejectsLocator(bool hidden)
    {
        (SharedComponentVersion v1, _, string hash) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow consumer = LinkedConsumer(hash, _baseBlocks);
        Section linked = consumer.Sections.Single(s => s.SharedComponentId is not null);
        if (hidden)
        {
            _ = linked.Blocks.Remove(linked.Blocks.Single(b => b.ReplacementTag == "rate"));
        }

        linked.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["rate"] = hidden ? SharedComponentSlotState.Hidden : SharedComponentSlotState.BrokenOut,
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id, entryKey: "rate")]),
            TestContext.Current.CancellationToken);

        AssertEntryLocatorRejected(result);
    }

    [Fact]
    public async Task Apply_WholeSectionUpdateShared_RefEntryLocalAhead_ConflictingDecisions()
    {
        (SharedComponentVersion secHead, _, string kycHash, string secHash) = await SeedRefEntrySectionComponentAsync();
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: true);
        Block innerBefore = consumer.Sections.Single(s => s.SharedComponentId is not null)
            .Blocks.Single(b => b.SharedComponentId == "c-kyc");
        ((AiQueryProperties)innerBefore.Properties).UserPromptTemplate = "KYC improved";
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, secHead.Id)]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.CONFLICTING_DECISIONS, result.FirstError.Description);
        Assert.Contains("decide the Library-linked block first", result.FirstError.Description);
        Assert.Equal(1, await _coreContext.SharedComponentVersions.AsNoTracking()
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_WholeSectionUpdateShared_UnchangedRefEntryStoredBeforeAFieldWasDropped_Pushes()
    {
        (SharedComponentVersion secHead, _, string kycHash, string secHash) =
            await SeedRefEntrySectionComponentAsync(kycStoredBeforeAFieldWasDropped: true);
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: true);
        Block rate = consumer.Sections.Single(s => s.SharedComponentId is not null)
            .Blocks.Single(b => b.ReplacementTag == "rate");
        ((AiQueryProperties)rate.Properties).UserPromptTemplate = "Extract the FX rate at the period end";
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(instance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, secHead.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Assert.Equal(2, await _coreContext.SharedComponentVersions.AsNoTracking()
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
        Workflow anchorAfter = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(ANCHOR))!;
        Block inner = anchorAfter.GetAllBlocks().Single(b => b.SharedComponentId == "c-kyc");
        Assert.Equal(kycHash, inner.SharedComponentVersionHash);
        Assert.Equal("KYC v1", ((AiQueryProperties)inner.Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_UpdateShared_SiblingCopyOfAVersionStoredBeforeAFieldWasDropped_TakesThePushWithoutAsking()
    {
        (SharedComponentVersion head, string hash) = await SeedBlockComponentAsync(
            "Who signs?", storedBeforeAFieldWasDropped: true);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, BlockConsumer(
            hash, "Who signs and countersigns?", SharedComponentLinkState.Linked));
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", SIBLING, BlockConsumer(
            hash, "Who signs?", SharedComponentLinkState.Linked));
        Block before = await LinkedBlockAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableBlockId!, SharedComponentReconcileAction.UpdateShared, head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Block sibling = await LinkedBlockAsync(SIBLING);
        Assert.Equal("Who signs and countersigns?", ((AiQueryProperties)sibling.Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_UpdateShared_FromACopySetToStopTheRun_PushesTheContentButNotTheFailureSetting()
    {
        (SharedComponentVersion head, string hash) = await SeedBlockComponentAsync("Who signs?");
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, BlockConsumer(
            hash, "Who signs and countersigns?", SharedComponentLinkState.Linked, isCritical: true));
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", SIBLING, BlockConsumer(
            hash, "Who signs?", SharedComponentLinkState.Linked, isCritical: false));
        Block before = await LinkedBlockAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableBlockId!, SharedComponentReconcileAction.UpdateShared, head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Block sibling = await LinkedBlockAsync(SIBLING);
        Assert.Equal("Who signs and countersigns?", ((AiQueryProperties)sibling.Properties).UserPromptTemplate);
        Assert.False(sibling.Properties.IsCritical);
        Assert.True((await LinkedBlockAsync(ANCHOR)).Properties.IsCritical);
        List<SharedComponentVersion> versions = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .Where(v => v.SharedComponentId == COMPONENT_ID)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);
        Assert.All(versions, v => Assert.DoesNotContain(nameof(BlockProperties.IsCritical), v.ContentJson));
    }

    [Fact]
    public async Task Apply_Relink_ForkThatDiffersOnlyInItsFailureSetting_RelinksAndKeepsIt()
    {
        (SharedComponentVersion head, string hash) = await SeedBlockComponentAsync("Who signs?");
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, BlockConsumer(
            hash, "Who signs?", SharedComponentLinkState.BrokenOut, isCritical: true));
        Block before = await LinkedBlockAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableBlockId!, SharedComponentReconcileAction.Relink, head.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Block reloaded = await LinkedBlockAsync(ANCHOR);
        Assert.Equal(SharedComponentLinkState.Linked, reloaded.SharedComponentLinkState);
        Assert.True(reloaded.Properties.IsCritical);
    }

    [Fact]
    public async Task Apply_AdoptHead_KeepsEachBlocksOwnFailureSetting()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        (string jsonV2, string hashV2) = Definition(
            DefSection("Market Data", ("rate", "Extract the spot FX rate"), ("source", "Cite the source")));
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, jsonV2, parentVersionId: v1.Id);
        Workflow consumer = LinkedConsumer(hashV1, _baseBlocks);
        consumer.GetAllBlocks().Single(b => b.ReplacementTag == "rate").Properties.IsCritical = true;
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section before = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.AdoptHead, v2.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(hashV2, reloaded.SharedComponentVersionHash);
        Block rate = reloaded.Blocks.Single(b => b.ReplacementTag == "rate");
        Assert.Equal("Extract the spot FX rate", ((AiQueryProperties)rate.Properties).UserPromptTemplate);
        Assert.True(rate.Properties.IsCritical);
        Assert.Null(reloaded.Blocks.Single(b => b.ReplacementTag == "source").Properties.IsCritical);
    }

    [Fact]
    public async Task Apply_EntryBatch_ArmFailureMidBatch_AppliesNothing()
    {
        (SharedComponentVersion secHead, _, string kycHash, string secHash) = await SeedRefEntrySectionComponentAsync();
        Workflow consumer = RefEntryConsumer(secHash, kycHash, includeInner: true);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, consumer);
        Section instance = await LinkedSectionAsync(ANCHOR);
        int workflowVersionsBefore = await _coreContext.WorkflowVersions
            .CountAsync(TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.Hide, entryKey: "rate"),
                    Decision(instance.StableSectionId!, SharedComponentReconcileAction.AdoptHead, secHead.Id,
                        entryKey: "kyc", expectedRefHead: "bogus-version"),
                ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.HEAD_MOVED, result.FirstError.Code);
        Section reloaded = await LinkedSectionAsync(ANCHOR);
        Assert.Contains(reloaded.Blocks, b => b.ReplacementTag == "rate");
        Assert.Null(reloaded.SharedComponentSlotStates);
        Assert.Equal(workflowVersionsBefore, await _coreContext.WorkflowVersions
            .CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_UpdateSharedWithLocalAddition_DoesNotDuplicateTheAdditionInTheAnchor()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        Workflow anchorWorkflow = LinkedConsumer(hashV1, _baseBlocks);
        Section anchorSection = anchorWorkflow.Sections.Single(s => s.SharedComponentId == COMPONENT_ID);
        _ = new Block(BlockTypes.AiQuery, anchorSection, "local_note")
        {
            Name = "Local Note",
            Properties = new AiQueryProperties { UserPromptTemplate = "A locally added note" },
        };
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, anchorWorkflow);
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", SIBLING, LinkedConsumer(hashV1, _baseBlocks));
        Section before = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(before.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Section anchorAfter = await LinkedSectionAsync(ANCHOR);
        Assert.Equal(
            ["rate", "source", "local_note"],
            anchorAfter.Blocks.OrderBy(b => b.Order).Select(b => b.ReplacementTag));
        Section siblingAfter = await LinkedSectionAsync(SIBLING);
        Assert.Equal(
            ["rate", "source", "local_note"],
            siblingAfter.Blocks.OrderBy(b => b.Order).Select(b => b.ReplacementTag));
        Assert.Equal(
            "A locally added note",
            ((AiQueryProperties)siblingAfter.Blocks.Single(b => b.ReplacementTag == "local_note").Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Apply_DecisionOnOneComponent_RestampsStaleStampedInSyncInstanceOfAnother()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        (string secondJson1, string secondHash1) = Definition(BuildBlockDefinition("Second v1"));
        (string secondJson2, string secondHash2) = Definition(BuildBlockDefinition("Second v2"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Second", id: "c-second");
        SharedComponentVersion secondV1 = await _harness.AddVersionAsync("c-second", 1, secondHash1, secondJson1);
        _ = await _harness.AddVersionAsync("c-second", 2, secondHash2, secondJson2, parentVersionId: secondV1.Id);

        Workflow anchorWorkflow = LinkedConsumer(hashV1, [("rate", "Locally improved"), ("source", "Cite the source")]);
        AttachStaleStampedSecond(anchorWorkflow, secondHash1);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, anchorWorkflow);
        var outsideWorkflow = new Workflow("Outside") { Title = "Outside" };
        AttachStaleStampedSecond(outsideWorkflow, secondHash1);
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-OUTSIDE", outsideWorkflow);
        Section anchorInstance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(anchorInstance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow anchorAfter = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(ANCHOR))!;
        Block restamped = anchorAfter.GetAllBlocks().Single(b => b.SharedComponentId == "c-second");
        Assert.Equal(secondHash2, restamped.SharedComponentVersionHash);
        Assert.Equal("Second v2", ((AiQueryProperties)restamped.Properties).UserPromptTemplate);
        Assert.Equal(2, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == "c-second", TestContext.Current.CancellationToken));
        Workflow outsideAfter = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-OUTSIDE"))!;
        Block untouched = outsideAfter.GetAllBlocks().Single(b => b.SharedComponentId == "c-second");
        Assert.Equal(secondHash1, untouched.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Apply_DecisionOnOneComponent_RestampsInSyncInstanceOfAHeadStoredBeforeAFieldWasDropped()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        (string secondJson1, string secondHash1) = Definition(BuildBlockDefinition("Second v1"));
        (string secondJson2, string secondHash2) =
            _harness.StoredBeforeAFieldWasDropped(Definition(BuildBlockDefinition("Second v2")).DefinitionJson);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Second", id: "c-second");
        SharedComponentVersion secondV1 = await _harness.AddVersionAsync("c-second", 1, secondHash1, secondJson1);
        _ = await _harness.AddVersionAsync("c-second", 2, secondHash2, secondJson2, parentVersionId: secondV1.Id);

        Workflow anchorWorkflow = LinkedConsumer(hashV1, [("rate", "Locally improved"), ("source", "Cite the source")]);
        AttachStaleStampedSecond(anchorWorkflow, secondHash1);
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, anchorWorkflow);
        Section anchorInstance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(anchorInstance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : null);
        Workflow anchorAfter = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(ANCHOR))!;
        Block restamped = anchorAfter.GetAllBlocks().Single(b => b.SharedComponentId == "c-second");
        Assert.Equal(secondHash2, restamped.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Apply_StaleStampedInstanceWithDivergedContent_KeepsItsStamp()
    {
        (SharedComponentVersion v1, _, string hashV1) = await SeedSectionComponentAsync(_baseBlocks);
        (string secondJson1, string secondHash1) = Definition(BuildBlockDefinition("Second v1"));
        (string secondJson2, string secondHash2) = Definition(BuildBlockDefinition("Second v2"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Second", id: "c-second");
        SharedComponentVersion secondV1 = await _harness.AddVersionAsync("c-second", 1, secondHash1, secondJson1);
        _ = await _harness.AddVersionAsync("c-second", 2, secondHash2, secondJson2, parentVersionId: secondV1.Id);

        Workflow anchorWorkflow = LinkedConsumer(hashV1, [("rate", "Locally improved"), ("source", "Cite the source")]);
        AttachStaleStampedSecond(anchorWorkflow, secondHash1, prompt: "A local fork nobody pushed");
        string baseVersionId = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", ANCHOR, anchorWorkflow);
        Section anchorInstance = await LinkedSectionAsync(ANCHOR);

        ErrorOr<SharedComponentReconcileApplyResult> result = await _service.ApplyReconcileAsync(
            new ApplySharedComponentReconcileCommand(ORG, USER, ANCHOR, baseVersionId,
                [Decision(anchorInstance.StableSectionId!, SharedComponentReconcileAction.UpdateShared, v1.Id)]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow anchorAfter = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(ANCHOR))!;
        Block fork = anchorAfter.GetAllBlocks().Single(b => b.SharedComponentId == "c-second");
        Assert.Equal(secondHash1, fork.SharedComponentVersionHash);
        Assert.Equal("A local fork nobody pushed", ((AiQueryProperties)fork.Properties).UserPromptTemplate);
    }

    private static void AttachStaleStampedSecond(Workflow workflow, string stampHash, string prompt = "Second v2")
    {
        var home = new Section("Second Home", workflow) { Title = "Second Home" };
        var instance = new Block(BlockTypes.AiQuery, home, "second")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
        instance.SharedComponentId = "c-second";
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = stampHash;
    }

    private async Task<(SharedComponentVersion SecHead, SharedComponentVersion KycHead, string KycHash, string SectionHash)>
        SeedRefEntrySectionComponentAsync(bool kycStoredBeforeAFieldWasDropped = false)
    {
        (string kycJson, string kycHash) = Definition(BuildBlockDefinition("KYC v1"));
        if (kycStoredBeforeAFieldWasDropped)
        {
            (kycJson, kycHash) = _harness.StoredBeforeAFieldWasDropped(kycJson);
        }

        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: "c-kyc");
        SharedComponentVersion kycHead = await _harness.AddVersionAsync("c-kyc", 1, kycHash, kycJson);

        Section headDef = DefSection("Market Data", ("rate", "Extract the FX rate"));
        headDef.Entries =
        [
            new SectionEntry { SlotTag = "rate", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = "c-kyc" },
        ];
        (string secJson, string secHash) = Definition(headDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion secHead = await _harness.AddVersionAsync(COMPONENT_ID, 1, secHash, secJson);

        return (secHead, kycHead, kycHash, secHash);
    }

    private static Workflow RefEntryConsumer(string sectionHash, string kycHash, bool includeInner)
    {
        var workflow = new Workflow("Consumer") { Title = "Consumer" };
        var intro = new Section("Intro", workflow) { Title = "Intro" };
        _ = new Block(BlockTypes.AiQuery, intro, "intro_note")
        {
            Name = "Note",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarize" },
        };
        var linked = new Section("Market Data", workflow) { Title = "Market Data" };
        _ = new Block(BlockTypes.AiQuery, linked, "rate")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = "Extract the FX rate" },
        };
        if (includeInner)
        {
            var inner = new Block(BlockTypes.AiQuery, linked, "kyc")
            {
                Name = "Node",
                Properties = new AiQueryProperties { UserPromptTemplate = "KYC v1" },
            };
            inner.SharedComponentId = "c-kyc";
            inner.SharedComponentLinkState = SharedComponentLinkState.Linked;
            inner.SharedComponentVersionHash = kycHash;
        }

        linked.SharedComponentId = COMPONENT_ID;
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentVersionHash = sectionHash;

        return workflow;
    }

    private static void AssertEntryLocatorRejected(ErrorOr<SharedComponentReconcileApplyResult> result)
    {
        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.INVALID_TARGET_LOCATOR, result.FirstError.Description);
    }

    private static SharedComponentReconcileDecision Decision(
        string stableId,
        SharedComponentReconcileAction action,
        string? expectedHead = null,
        bool ack = false,
        string componentId = COMPONENT_ID,
        string? entryKey = null,
        string? entryStableId = null,
        string? expectedRefHead = null)
    {
        return new SharedComponentReconcileDecision(
            componentId,
            stableId,
            TargetPath: null,
            action,
            expectedHead,
            ack,
            entryKey,
            entryStableId,
            expectedRefHead);
    }

    private async Task<(SharedComponentVersion Head, string DefinitionJson, string Hash)> SeedSectionComponentAsync(
        (string Tag, string Prompt)[] blocks,
        bool declareSourceSignatureFields = false)
    {
        Section definitionSection = DefSection("Market Data", blocks);
        if (declareSourceSignatureFields)
        {
            DeclareDirectorSignature(definitionSection);
        }

        (string json, string hash) = Definition(definitionSection);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion head = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, json);

        return (head, json, hash);
    }

    private static void DeclareDirectorSignature(Section section)
    {
        Block source = section.Blocks.Single(b => b.ReplacementTag == "source");
        ((AiQueryProperties)source.Properties).SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["director_name"] = new("Director", "Who signs", "N/A"),
        };
    }

    private async Task<(SharedComponentVersion Head, string Hash)> SeedBlockComponentAsync(
        string prompt,
        bool storedBeforeAFieldWasDropped = false)
    {
        (string json, string hash) = Definition(BuildBlockDefinition(prompt));
        if (storedBeforeAFieldWasDropped)
        {
            (json, hash) = _harness.StoredBeforeAFieldWasDropped(json);
        }

        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer Check", id: COMPONENT_ID);
        SharedComponentVersion head = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, json);

        return (head, hash);
    }

    private async Task<string> VersionContentHashAsync(string versionId)
    {
        WorkflowVersion version = await _coreContext.WorkflowVersions
            .AsNoTracking()
            .SingleAsync(v => v.Id == versionId);

        return version.ContentHash;
    }

    private async Task<Section> LinkedSectionAsync(string workflowTypeId)
    {
        Workflow workflow = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId))!;

        return workflow.Sections.Single(s => s.SharedComponentId == COMPONENT_ID);
    }

    private async Task<Block> LinkedBlockAsync(string workflowTypeId)
    {
        Workflow workflow = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId))!;

        return workflow.GetAllBlocks().Single(b => b.SharedComponentId == COMPONENT_ID);
    }

    private (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        return _harness.Definition(section);
    }

    private (string DefinitionJson, string ContentHash) Definition(Block block)
    {
        return _harness.Definition(block);
    }

    private static Workflow LinkedConsumer(
        string versionHash,
        (string Tag, string Prompt)[] blocks,
        Dictionary<string, string>? bindings = null,
        SharedComponentLinkState linkState = SharedComponentLinkState.Linked)
    {
        var workflow = new Workflow("Consumer") { Title = "Consumer" };
        var intro = new Section("Intro", workflow) { Title = "Intro" };
        _ = new Block(BlockTypes.AiQuery, intro, "intro_note")
        {
            Name = "Note",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarize" },
        };
        Section linked = DefSection("Market Data", workflow, blocks);
        linked.SharedComponentId = COMPONENT_ID;
        linked.SharedComponentLinkState = linkState;
        linked.SharedComponentVersionHash = versionHash;
        linked.SharedComponentBindings = bindings;

        return workflow;
    }

    private static Workflow BlockConsumer(
        string versionHash,
        string prompt,
        SharedComponentLinkState linkState,
        bool? isCritical = null)
    {
        var workflow = new Workflow("Consumer") { Title = "Consumer" };
        var intro = new Section("Intro", workflow) { Title = "Intro" };
        _ = new Block(BlockTypes.AiQuery, intro, "intro_note")
        {
            Name = "Note",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarize" },
        };
        var body = new Section("Body", workflow) { Title = "Body" };
        var linked = new Block(BlockTypes.AiQuery, body, "signer")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt, IsCritical = isCritical },
        };
        linked.SharedComponentId = COMPONENT_ID;
        linked.SharedComponentLinkState = linkState;
        linked.SharedComponentVersionHash = versionHash;

        return workflow;
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
