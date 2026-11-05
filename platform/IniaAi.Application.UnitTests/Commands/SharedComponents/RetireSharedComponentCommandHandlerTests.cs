using ErrorOr;
using IniaAi.Application.Commands.SharedComponents.RetireSharedComponent;
using IniaAi.Application.Commands.SharedComponents.UnretireSharedComponent;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Application.UnitTests.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.UnitTests.Commands.SharedComponents;

public class RetireSharedComponentCommandHandlerTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-retire";
    private const string COMPONENT_ID = "component-1";
    private const string CONTAINER_ID = "container-1";
    private const string FOREIGN_REF_ID = "component-2";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly CoreDbContext _coreContext;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowSavePipeline _pipeline;
    private readonly SharedComponentFanOutService _service;
    private readonly SaveWorkflowCommandHandler _saveHandler;

    public RetireSharedComponentCommandHandlerTests()
    {
        _coreContext = _harness.Context;
        _coreUnitOfWork = _harness.UnitOfWork;
        _pipeline = _harness.CreatePipeline();
        _service = _harness.CreateFanOutService(_pipeline);
        (_, _saveHandler) = _harness.CreateRealSaveSender();
    }

    [Fact]
    public async Task Retire_BreaksOutEveryLinkedInstanceAndRetires()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Extract the FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, jsonV1);
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", LinkedConsumer(hashV1, instanceCount: 2));
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-B", LinkedConsumer(hashV1, instanceCount: 1));
        string baseHashA = (await _coreContext.WorkflowVersions
            .AsNoTracking()
            .Where(v => v.WorkflowTypeId == "WT-A")
            .OrderByDescending(v => v.CreatedAt)
            .FirstAsync(TestContext.Current.CancellationToken)).ContentHash;

        var handler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(SharedComponentState.Retired, result.Value.State);
        Assert.Equal(3, result.Value.BrokenOutInstanceCount);
        Assert.Equal(2, result.Value.WorkflowSaves.Count);
        SharedComponent component = await _coreContext.SharedComponents
            .AsNoTracking()
            .SingleAsync(c => c.Id == COMPONENT_ID, TestContext.Current.CancellationToken);
        Assert.Equal(SharedComponentState.Retired, component.State);
        Workflow reloadedA = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        foreach (Section instance in reloadedA.Sections.Where(s => s.SharedComponentId == COMPONENT_ID))
        {
            Assert.Equal(SharedComponentLinkState.BrokenOut, instance.SharedComponentLinkState);
            Assert.Equal(hashV1, instance.SharedComponentVersionHash);
        }

        SharedComponentConsumerSaveResult saveA = result.Value.WorkflowSaves.Single(s => s.WorkflowTypeId == "WT-A");
        Assert.Equal(baseHashA, saveA.ContentHash);
        Assert.False(saveA.WasDeduped);
        WorkflowVersion headA = await _coreContext.WorkflowVersions
            .AsNoTracking()
            .SingleAsync(v => v.Id == saveA.VersionId, TestContext.Current.CancellationToken);
        Assert.Contains("retired", headA.AutoNarrativeSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _service.GetConsumersAsync(COMPONENT_ID, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Retire_AlreadyRetired_IsIdempotentNoOp()
    {
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", SharedComponentState.Retired, id: COMPONENT_ID);
        int workflowVersionsBefore = await _coreContext.WorkflowVersions
            .CountAsync(TestContext.Current.CancellationToken);

        var handler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(SharedComponentState.Retired, result.Value.State);
        Assert.Equal(0, result.Value.BrokenOutInstanceCount);
        Assert.Empty(result.Value.WorkflowSaves);
        Assert.Equal(workflowVersionsBefore, await _coreContext.WorkflowVersions
            .CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Retire_ConsumerSaveRaces_NothingFlips()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Extract the FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, jsonV1);
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", LinkedConsumer(hashV1, instanceCount: 1));
        SharedComponentFanOutService racingService = _harness.CreateFanOutService(
            new ThrowingWorkflowSavePipeline(_harness, () => new DbUpdateConcurrencyException("raced")));

        var handler = new RetireSharedComponentCommandHandler(racingService);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(WorkflowSavePipeline.CONCURRENT_SAVE_CODE, result.FirstError.Code);
        SharedComponent component = await _coreContext.SharedComponents
            .AsNoTracking()
            .SingleAsync(c => c.Id == COMPONENT_ID, TestContext.Current.CancellationToken);
        Assert.Equal(SharedComponentState.Active, component.State);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Assert.Equal(
            SharedComponentLinkState.Linked,
            reloaded.Sections.Single(s => s.SharedComponentId == COMPONENT_ID).SharedComponentLinkState);
    }

    [Fact]
    public async Task Unretire_ReactivatesWithoutRelinkingAnyone()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Extract the FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, jsonV1);
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", LinkedConsumer(hashV1, instanceCount: 1));
        var retireHandler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> retired = await retireHandler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);
        Assert.False(retired.IsError);

        var handler = new UnretireSharedComponentCommandHandler(_coreUnitOfWork);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new UnretireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(SharedComponentState.Active, result.Value.State);
        Assert.Equal(0, result.Value.BrokenOutInstanceCount);
        Assert.Empty(result.Value.WorkflowSaves);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Assert.Equal(
            SharedComponentLinkState.BrokenOut,
            reloaded.Sections.Single(s => s.SharedComponentId == COMPONENT_ID).SharedComponentLinkState);
        Assert.Empty(await _service.GetConsumersAsync(COMPONENT_ID, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Retire_ReferencedByContainerDefinition_InlinesRefAndRestampsConsumer()
    {
        (string kycJson, string kycHash) = Definition(BlockDefinition("KYC v1"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, kycHash, kycJson);

        Section containerDef = DefinitionSection("Risk", ("intro", "Intro prompt"));
        containerDef.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = COMPONENT_ID },
        ];
        (string containerJson, string containerHash) = Definition(containerDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: CONTAINER_ID);
        SharedComponentVersion containerHead = await _harness.AddVersionAsync(CONTAINER_ID, 1, containerHash, containerJson);

        var workflow = new Workflow("Consumer") { Title = "Consumer" };
        Section instance = SectionWithBlocks("Risk", workflow, ("intro", "Intro prompt"));
        _ = LinkedInnerBlock(instance, "kyc", "KYC v1", COMPONENT_ID, kycHash);
        instance.SharedComponentId = CONTAINER_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = containerHash;
        instance.SharedComponentBindings = new Dictionary<string, string> { ["intro"] = "intro", ["kyc"] = "kyc" };
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-S", workflow);
        Workflow seeded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-S"))!;
        Section seededInstance = seeded.Sections.Single(s => s.SharedComponentId == CONTAINER_ID);
        Assert.Equal(
            containerHash,
            _harness.Normalizer.ComputeContentHash(seededInstance, seededInstance.SharedComponentBindings));

        var handler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(SharedComponentState.Retired, result.Value.State);
        Assert.Equal(1, result.Value.BrokenOutInstanceCount);
        Assert.Equal([CONTAINER_ID], result.Value.InlinedContainerIds);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.Equal("WT-S", save.WorkflowTypeId);

        List<SharedComponentVersion> containerVersions = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .Where(v => v.SharedComponentId == CONTAINER_ID)
            .OrderBy(v => v.SequenceNumber)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, containerVersions.Count);
        SharedComponentVersion newHead = containerVersions[1];
        Assert.Equal(containerHead.Id, newHead.ParentVersionId);
        Assert.Equal(USER, newHead.CreatedByUserId);

        Section expectedDef = DefinitionSection("Risk", ("intro", "Intro prompt"), ("kyc", "KYC v1"));
        (string expectedJson, string expectedHash) = Definition(expectedDef);
        Assert.Equal(expectedHash, newHead.ContentHash);
        Assert.Equal(expectedJson, newHead.ContentJson);
        Assert.DoesNotContain("\"Entries\"", newHead.ContentJson);

        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-S"))!;
        Section reloadedInstance = reloaded.Sections.Single(s => s.SharedComponentId == CONTAINER_ID);
        Assert.Equal(SharedComponentLinkState.Linked, reloadedInstance.SharedComponentLinkState);
        Assert.Equal(expectedHash, reloadedInstance.SharedComponentVersionHash);
        Block reloadedKyc = reloadedInstance.Blocks.Single(b => b.ReplacementTag == "kyc");
        Assert.Equal(COMPONENT_ID, reloadedKyc.SharedComponentId);
        Assert.Equal(SharedComponentLinkState.BrokenOut, reloadedKyc.SharedComponentLinkState);
        Assert.Equal(kycHash, reloadedKyc.SharedComponentVersionHash);
        Assert.Equal(
            expectedHash,
            _harness.Normalizer.ComputeContentHash(reloadedInstance, reloadedInstance.SharedComponentBindings));
    }

    [Fact]
    public async Task Retire_RefEntryWithBindings_BakedBlockRenamesPlaceholderReferences()
    {
        (string judgeJson, string judgeHash) = Definition(BlockDefinition("Use {rate} for conversion."));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Rate Judge", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, judgeHash, judgeJson);

        Section containerDef = DefinitionSection("Risk", ("fx_rate", "Extract the FX rate"));
        containerDef.Entries =
        [
            new SectionEntry { SlotTag = "fx_rate", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "judge",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = COMPONENT_ID,
                Bindings = new Dictionary<string, string> { ["rate"] = "fx_rate" },
            },
        ];
        (string containerJson, string containerHash) = Definition(containerDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: CONTAINER_ID);
        _ = await _harness.AddVersionAsync(CONTAINER_ID, 1, containerHash, containerJson);

        var handler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.BrokenOutInstanceCount);
        Assert.Equal([CONTAINER_ID], result.Value.InlinedContainerIds);
        Assert.Empty(result.Value.WorkflowSaves);
        SharedComponentVersion newHead = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(
                v => v.SharedComponentId == CONTAINER_ID && v.SequenceNumber == 2,
                TestContext.Current.CancellationToken);
        Section expectedDef = DefinitionSection(
            "Risk", ("fx_rate", "Extract the FX rate"), ("judge", "Use {fx_rate} for conversion."));
        (string expectedJson, string expectedHash) = Definition(expectedDef);
        Assert.Equal(expectedHash, newHead.ContentHash);
        Assert.Equal(expectedJson, newHead.ContentJson);
    }

    [Fact]
    public async Task Retire_ContainerWithForeignRef_KeepsEntriesAndOtherRefIntact()
    {
        (string kycJson, string kycHash) = Definition(BlockDefinition("KYC v1"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, kycHash, kycJson);
        (string amlJson, string amlHash) = Definition(BlockDefinition("AML screening"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "AML Screen", id: FOREIGN_REF_ID);
        _ = await _harness.AddVersionAsync(FOREIGN_REF_ID, 1, amlHash, amlJson);

        var containerDef = new Section("Risk", new Workflow("Scratch")) { Title = "Risk" };
        containerDef.Entries =
        [
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = COMPONENT_ID },
            new SectionEntry
            {
                SlotTag = "aml",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = FOREIGN_REF_ID,
                Bindings = new Dictionary<string, string> { ["screen"] = "kyc" },
            },
        ];
        (string containerJson, string containerHash) = Definition(containerDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: CONTAINER_ID);
        _ = await _harness.AddVersionAsync(CONTAINER_ID, 1, containerHash, containerJson);

        var handler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal([CONTAINER_ID], result.Value.InlinedContainerIds);
        SharedComponentVersion newHead = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(
                v => v.SharedComponentId == CONTAINER_ID && v.SequenceNumber == 2,
                TestContext.Current.CancellationToken);
        Section parsed = Section.FromJson(newHead.ContentJson)!;
        Assert.NotNull(parsed.Entries);
        Assert.Equal(2, parsed.Entries.Count);
        Assert.Equal(SectionEntryKind.Inline, parsed.Entries[0].Kind);
        Assert.Equal("kyc", parsed.Entries[0].SlotTag);
        Assert.True(string.IsNullOrEmpty(parsed.Entries[0].SharedComponentId));
        Assert.Null(parsed.Entries[0].Bindings);
        Assert.Equal(SectionEntryKind.ComponentRef, parsed.Entries[1].Kind);
        Assert.Equal(FOREIGN_REF_ID, parsed.Entries[1].SharedComponentId);
        Assert.Equal("kyc", parsed.Entries[1].Bindings!["screen"]);
        Block baked = Assert.Single(parsed.Blocks);
        Assert.Equal("kyc", baked.ReplacementTag);
        Assert.Equal(0, baked.Order);
    }

    [Fact]
    public async Task Retire_RetiredContainerDefinition_IsAlsoInlined()
    {
        (string kycJson, string kycHash) = Definition(BlockDefinition("KYC v1"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, kycHash, kycJson);

        var containerDef = new Section("Risk", new Workflow("Scratch")) { Title = "Risk" };
        containerDef.Entries =
        [
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = COMPONENT_ID },
        ];
        (string containerJson, string containerHash) = Definition(containerDef);
        await _harness.AddComponentAsync(
            SharedComponentKind.Section, "Risk Pack", SharedComponentState.Retired, id: CONTAINER_ID);
        _ = await _harness.AddVersionAsync(CONTAINER_ID, 1, containerHash, containerJson);

        var handler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal([CONTAINER_ID], result.Value.InlinedContainerIds);
        Assert.Empty(result.Value.WorkflowSaves);
        SharedComponent container = await _coreContext.SharedComponents
            .AsNoTracking()
            .SingleAsync(c => c.Id == CONTAINER_ID, TestContext.Current.CancellationToken);
        Assert.Equal(SharedComponentState.Retired, container.State);
        SharedComponentVersion newHead = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(
                v => v.SharedComponentId == CONTAINER_ID && v.SequenceNumber == 2,
                TestContext.Current.CancellationToken);
        Section parsed = Section.FromJson(newHead.ContentJson)!;
        Assert.Null(parsed.Entries);
        Block baked = Assert.Single(parsed.Blocks);
        Assert.Equal("kyc", baked.ReplacementTag);
    }

    [Fact]
    public async Task Retire_HiddenRefSlot_RestampsAndReconstructsInSync()
    {
        (string kycJson, string kycHash) = Definition(BlockDefinition("KYC v1"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, kycHash, kycJson);

        Section containerDef = DefinitionSection("Risk", ("intro", "Intro prompt"));
        containerDef.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = COMPONENT_ID },
        ];
        (string containerJson, string containerHash) = Definition(containerDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: CONTAINER_ID);
        _ = await _harness.AddVersionAsync(CONTAINER_ID, 1, containerHash, containerJson);

        var workflow = new Workflow("Consumer") { Title = "Consumer" };
        Section instance = SectionWithBlocks("Risk", workflow, ("intro", "Intro prompt"));
        instance.SharedComponentId = CONTAINER_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = containerHash;
        instance.SharedComponentBindings = new Dictionary<string, string> { ["intro"] = "intro" };
        instance.SharedComponentSlotStates = new Dictionary<string, SharedComponentSlotState>
        {
            ["kyc"] = SharedComponentSlotState.Hidden,
        };
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-H", workflow);

        var handler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.BrokenOutInstanceCount);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.WorkflowSaves);
        Assert.Equal("WT-H", save.WorkflowTypeId);
        WorkflowVersion consumerHead = await _coreContext.WorkflowVersions
            .AsNoTracking()
            .SingleAsync(v => v.Id == save.VersionId, TestContext.Current.CancellationToken);
        Assert.Contains("Inlined retired component", consumerHead.AutoNarrativeSummary);

        SharedComponentVersion newHead = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(
                v => v.SharedComponentId == CONTAINER_ID && v.SequenceNumber == 2,
                TestContext.Current.CancellationToken);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-H"))!;
        Section reloadedInstance = reloaded.Sections.Single(s => s.SharedComponentId == CONTAINER_ID);
        Assert.Equal(newHead.ContentHash, reloadedInstance.SharedComponentVersionHash);
        Assert.Equal(
            SharedComponentSlotState.Hidden,
            reloadedInstance.SharedComponentSlotStates!["kyc"]);
        Assert.DoesNotContain(reloadedInstance.Blocks, b => b.ReplacementTag == "kyc");
        string? reconstructed = _harness.Normalizer.ReconstructDefinitionJson(
            reloadedInstance, Section.FromJson(newHead.ContentJson));
        Assert.NotNull(reconstructed);
        Assert.Equal(newHead.ContentHash, _harness.Normalizer.ComputeContentHashOfDefinitionJson(reconstructed));
    }

    [Fact]
    public async Task Retire_ContainerRestampSaveRaces_NothingPersists()
    {
        (string kycJson, string kycHash) = Definition(BlockDefinition("KYC v1"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC Check", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, kycHash, kycJson);

        Section containerDef = DefinitionSection("Risk", ("intro", "Intro prompt"));
        containerDef.Entries =
        [
            new SectionEntry { SlotTag = "intro", Kind = SectionEntryKind.Inline },
            new SectionEntry { SlotTag = "kyc", Kind = SectionEntryKind.ComponentRef, SharedComponentId = COMPONENT_ID },
        ];
        (string containerJson, string containerHash) = Definition(containerDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Risk Pack", id: CONTAINER_ID);
        _ = await _harness.AddVersionAsync(CONTAINER_ID, 1, containerHash, containerJson);

        var workflow = new Workflow("Consumer") { Title = "Consumer" };
        Section instance = SectionWithBlocks("Risk", workflow, ("intro", "Intro prompt"));
        _ = LinkedInnerBlock(instance, "kyc", "KYC v1", COMPONENT_ID, kycHash);
        instance.SharedComponentId = CONTAINER_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = containerHash;
        instance.SharedComponentBindings = new Dictionary<string, string> { ["intro"] = "intro", ["kyc"] = "kyc" };
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-R", workflow);
        SharedComponentFanOutService racingService = _harness.CreateFanOutService(
            new ThrowingWorkflowSavePipeline(_harness, () => new DbUpdateConcurrencyException("raced")));

        var handler = new RetireSharedComponentCommandHandler(racingService);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(WorkflowSavePipeline.CONCURRENT_SAVE_CODE, result.FirstError.Code);
        SharedComponent component = await _coreContext.SharedComponents
            .AsNoTracking()
            .SingleAsync(c => c.Id == COMPONENT_ID, TestContext.Current.CancellationToken);
        Assert.Equal(SharedComponentState.Active, component.State);
        int containerVersionCount = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .CountAsync(v => v.SharedComponentId == CONTAINER_ID, TestContext.Current.CancellationToken);
        Assert.Equal(1, containerVersionCount);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-R"))!;
        Section reloadedInstance = reloaded.Sections.Single(s => s.SharedComponentId == CONTAINER_ID);
        Assert.Equal(containerHash, reloadedInstance.SharedComponentVersionHash);
        Assert.Equal(
            SharedComponentLinkState.Linked,
            reloadedInstance.Blocks.Single(b => b.ReplacementTag == "kyc").SharedComponentLinkState);
    }

    [Fact]
    public async Task RetireAndUnretire_UnknownComponent_ReturnNotFound()
    {
        var retireHandler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> retire = await retireHandler.Handle(
            new RetireSharedComponentCommand(ORG, USER, "missing"),
            TestContext.Current.CancellationToken);

        Assert.True(retire.IsError);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, retire.FirstError.Code);

        var unretireHandler = new UnretireSharedComponentCommandHandler(_coreUnitOfWork);
        ErrorOr<SharedComponentRetireResult> unretire = await unretireHandler.Handle(
            new UnretireSharedComponentCommand(ORG, USER, "missing"),
            TestContext.Current.CancellationToken);

        Assert.True(unretire.IsError);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, unretire.FirstError.Code);
    }

    [Fact]
    public async Task Retire_ConsumerCarriesAgentBlock_DisabledRuntime_RefusesNamingTheConsumer()
    {
        (string jsonV1, string hashV1) = Definition(DefSection("Extract the FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, jsonV1);
        (_, SaveWorkflowCommandHandler enabledSave) = _harness.CreateRealSaveSender(AgentTestData.EnabledRuntime());
        Workflow consumer = LinkedConsumer(hashV1, instanceCount: 1);
        var agents = new Section("Agents", consumer) { Title = "Agents" };
        _ = AgentTestData.AddAgent(agents, "extraction_agent");
        await _harness.SaveTemplateAsync(enabledSave, ORG, "user-seed", "WT-AGENT", consumer);

        var handler = new RetireSharedComponentCommandHandler(_service);
        ErrorOr<SharedComponentRetireResult> result = await handler.Handle(
            new RetireSharedComponentCommand(ORG, USER, COMPONENT_ID),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e =>
            e.Description.Contains("WT-AGENT", StringComparison.Ordinal)
            && e.Description.Contains("agent runtime", StringComparison.OrdinalIgnoreCase));
        SharedComponent component = await _coreContext.SharedComponents.AsNoTracking()
            .SingleAsync(c => c.Id == COMPONENT_ID, TestContext.Current.CancellationToken);
        Assert.Equal(SharedComponentState.Active, component.State);
    }

    private (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        return _harness.Definition(section);
    }

    private (string DefinitionJson, string ContentHash) Definition(Block block)
    {
        return _harness.Definition(block);
    }

    private static Section DefSection(string ratePrompt)
    {
        var section = new Section("Market Data", new Workflow("Scratch")) { Title = "Market Data" };
        _ = new Block(BlockTypes.AiQuery, section, "rate")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = ratePrompt },
        };

        return section;
    }

    private static Section SectionWithBlocks(string name, Workflow parent, params (string Tag, string Prompt)[] blocks)
    {
        var section = new Section(name, parent) { Title = name };
        foreach ((string tag, string prompt) in blocks)
        {
            _ = new Block(BlockTypes.AiQuery, section, tag)
            {
                Name = "Node",
                Properties = new AiQueryProperties { UserPromptTemplate = prompt },
            };
        }

        return section;
    }

    private static Section DefinitionSection(string name, params (string Tag, string Prompt)[] blocks)
    {
        return SectionWithBlocks(name, new Workflow("Scratch"), blocks);
    }

    private static Block BlockDefinition(string prompt)
    {
        var scratch = new Section("Scratch", new Workflow("Scratch")) { Title = "Scratch" };

        return new Block(BlockTypes.AiQuery, scratch, "definition")
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    private static Block LinkedInnerBlock(
        Section section,
        string tag,
        string prompt,
        string componentId,
        string versionHash)
    {
        var block = new Block(BlockTypes.AiQuery, section, tag)
        {
            Name = "Node",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
        block.SharedComponentId = componentId;
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = versionHash;

        return block;
    }

    private static Workflow LinkedConsumer(string versionHash, int instanceCount)
    {
        var workflow = new Workflow("Consumer") { Title = "Consumer" };
        for (int i = 0; i < instanceCount; i++)
        {
            var section = new Section("Market Data", workflow) { Title = "Market Data" };
            _ = new Block(BlockTypes.AiQuery, section, $"rate_{i}")
            {
                Name = "Node",
                Properties = new AiQueryProperties { UserPromptTemplate = "Extract the FX rate" },
            };
            section.SharedComponentId = COMPONENT_ID;
            section.SharedComponentLinkState = SharedComponentLinkState.Linked;
            section.SharedComponentVersionHash = versionHash;
        }

        return workflow;
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
