using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;

namespace IniaAi.Application.UnitTests.Common.Workflows;

public class SharedComponentFanOutServiceAppendTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-apply";
    private const string COMPONENT_ID = "component-1";
    private const string REF_COMPONENT_ID = "component-ref-1";
    private const string OTHER_COMPONENT_ID = "component-2";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly CoreDbContext _coreContext;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowSavePipeline _pipeline;
    private readonly SharedComponentFanOutService _service;
    private readonly SaveWorkflowCommandHandler _saveHandler;

    public SharedComponentFanOutServiceAppendTests()
    {
        _coreContext = _harness.Context;
        _coreUnitOfWork = _harness.UnitOfWork;
        _pipeline = _harness.CreatePipeline();
        _service = _harness.CreateFanOutService(_pipeline);
        (_, _saveHandler) = _harness.CreateRealSaveSender();
    }

    [Fact]
    public async Task Append_BlockKind_ReappliesInstanceBindingRenames()
    {
        var scratchWorkflow = new Workflow("scratch");
        var scratchSection = new Section("scratch", scratchWorkflow);
        var defV1 = new Block(BlockTypes.AiQuery, scratchSection, "slot")
        {
            Name = "KYC",
            Properties = new AiQueryProperties { UserPromptTemplate = "Check {b_input}" },
        };
        _ = new TextInput(defV1, "b_input", "seed");
        (string defJsonV1, string hashV1) = Definition(defV1);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, defJsonV1);

        var consumer = new Workflow("Consumer") { Title = "Consumer", WorkflowTypeId = "WT-BIND" };
        var body = new Section("Body", consumer) { Title = "Body" };
        var instance = new Block(BlockTypes.AiQuery, body, "kyc")
        {
            Name = "KYC",
            Properties = new AiQueryProperties { UserPromptTemplate = "Check {my_input}" },
        };
        _ = new TextInput(instance, "my_input", "seed");
        instance.SharedComponentId = COMPONENT_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = hashV1;
        instance.SharedComponentBindings = new Dictionary<string, string> { ["b_input"] = "my_input" };
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-BIND", consumer);

        var defV2 = new Block(BlockTypes.AiQuery, scratchSection, "slot2")
        {
            Name = "KYC",
            Properties = new AiQueryProperties { UserPromptTemplate = "Check {b_input} twice" },
        };
        _ = new TextInput(defV2, "b_input", "seed");
        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, defV2.ToJson(pretty: false), v1.Id, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-BIND"))!;
        Block after = reloaded.GetAllBlocks().Single(b => b.SharedComponentId == COMPONENT_ID);
        Assert.Equal("Check {my_input} twice", ((AiQueryProperties)after.Properties).UserPromptTemplate);
        TextInput input = Assert.Single(after.TextInputs);
        Assert.Equal("my_input", input.ReplacementTag);
        Assert.Equal("my_input", after.SharedComponentBindings!["b_input"]);
    }

    [Fact]
    public async Task Append_SectionKind_AppendsVersionAndGraftsEveryLinkedConsumer()
    {
        (string definitionJsonV1, string hashV1) = Definition(BuildSectionDefinition());
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, definitionJsonV1);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Unrelated", id: OTHER_COMPONENT_ID);

        Workflow consumerA = BuildLinkedConsumer(hashV1);
        Block unrelated = consumerA.Sections.First(s => s.Title == "Intro").Blocks.First();
        unrelated.SharedComponentId = OTHER_COMPONENT_ID;
        unrelated.SharedComponentLinkState = SharedComponentLinkState.Linked;
        unrelated.SharedComponentVersionHash = new string('b', 64);
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", consumerA);
        Workflow consumerB = BuildLinkedConsumer(
            hashV1,
            instanceRateTag: "rate_2",
            bindings: new Dictionary<string, string> { ["rate"] = "rate_2" });
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-B", consumerB);
        Dictionary<string, string> idsBeforeA = await LinkedSectionStableIdsByTagAsync("WT-A");
        Dictionary<string, string> idsBeforeB = await LinkedSectionStableIdsByTagAsync("WT-B");

        Section submitted = BuildSectionDefinition("Extract the spot FX rate", includeCurrency: true);
        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, submitted.ToJson(pretty: false), v1.Id, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        (string expectedJsonV2, string expectedHashV2) =
            Definition(BuildSectionDefinition("Extract the spot FX rate", includeCurrency: true));
        List<SharedComponentVersion> versions = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .Where(v => v.SharedComponentId == COMPONENT_ID)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);
        SharedComponentVersion v2 = versions.Single(v => v.SequenceNumber == 2);
        Assert.Equal(v1.Id, v2.ParentVersionId);
        Assert.Equal(USER, v2.CreatedByUserId);
        Assert.Equal(expectedHashV2, v2.ContentHash);
        Assert.Equal(expectedJsonV2, v2.ContentJson);
        Assert.Equal(v2.Id, result.Value.HeadVersionId);
        Assert.Equal(expectedHashV2, result.Value.HeadContentHash);
        Assert.False(result.Value.WasDeduped);
        Assert.Equal(2, result.Value.ConsumerSaves.Count);

        Workflow reloadedA = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Section linkedA = reloadedA.Sections.Single(s => s.SharedComponentId == COMPONENT_ID);
        Assert.Equal(expectedHashV2, linkedA.SharedComponentVersionHash);
        Assert.Equal(SharedComponentLinkState.Linked, linkedA.SharedComponentLinkState);
        Block rateA = linkedA.Blocks.Single(b => b.ReplacementTag == "rate");
        Assert.Equal("Extract the spot FX rate", ((AiQueryProperties)rateA.Properties).UserPromptTemplate);
        Assert.Equal(idsBeforeA["rate"], rateA.StableBlockId);
        Assert.Equal(idsBeforeA["source"], linkedA.Blocks.Single(b => b.ReplacementTag == "source").StableBlockId);
        Block currencyA = linkedA.Blocks.Single(b => b.ReplacementTag == "currency");
        Assert.False(string.IsNullOrEmpty(currencyA.StableBlockId));
        Block reloadedUnrelated = reloadedA.Sections.Single(s => s.Title == "Intro").Blocks.First();
        Assert.Equal(new string('b', 64), reloadedUnrelated.SharedComponentVersionHash);

        SharedComponentConsumerSaveResult saveA = result.Value.ConsumerSaves.Single(s => s.WorkflowTypeId == "WT-A");
        WorkflowVersion headA = await _coreContext.WorkflowVersions
            .AsNoTracking()
            .SingleAsync(v => v.Id == saveA.VersionId, TestContext.Current.CancellationToken);
        Assert.Equal(
            $"Updated from shared component 'Market Data' ({expectedHashV2[..8]})",
            headA.AutoNarrativeSummary);

        Workflow reloadedB = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-B"))!;
        Section linkedB = reloadedB.Sections.Single(s => s.SharedComponentId == COMPONENT_ID);
        Assert.Equal(expectedHashV2, linkedB.SharedComponentVersionHash);
        Block rateB = linkedB.Blocks.Single(b => b.ReplacementTag == "rate_2");
        Assert.Equal("Extract the spot FX rate", ((AiQueryProperties)rateB.Properties).UserPromptTemplate);
        Assert.Equal(idsBeforeB["rate_2"], rateB.StableBlockId);
        Assert.Equal("rate_2", linkedB.SharedComponentBindings!["rate"]);
    }

    [Fact]
    public async Task Append_BlockKind_GraftsEveryLinkedInstance_WiringTravelsTagsPreserved()
    {
        (string definitionJsonV1, string hashV1) = Definition(BuildBlockDefinition("Who signs?"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer Check", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, definitionJsonV1);

        var consumer = new Workflow("Consumer") { Title = "Consumer" };
        var section = new Section("Body", consumer) { Title = "Body" };
        Block first = LinkedBlock(section, "signer_main", "Who signs?", hashV1);
        first.SupportedDocumentClasses = ["contract"];
        Block second = LinkedBlock(section, "signer_backup", "Who signs?", hashV1);
        _ = new Block(BlockTypes.AiQuery, section, "other")
        {
            Name = "Other",
            Properties = new AiQueryProperties { UserPromptTemplate = "Untouched" },
        };
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", consumer);
        Dictionary<string, string> idsBefore = await AllStableIdsByTagAsync("WT-A");

        Block submitted = BuildBlockDefinition("Who countersigns?");
        submitted.SupportedDocumentClasses = ["kyc"];
        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG,
            USER,
            COMPONENT_ID,
            submitted.ToJson(pretty: false),
            v1.Id,
            acknowledgeSignatureOrphans: false,
            TestContext.Current.CancellationToken,
            pendingRefTarget: null,
            acknowledgeOverwrittenEntries: true);

        Assert.False(result.IsError);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.ConsumerSaves);
        Assert.Equal("WT-A", save.WorkflowTypeId);

        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Section reloadedSection = reloaded.Sections.Single();
        Block main = reloadedSection.Blocks.Single(b => b.ReplacementTag == "signer_main");
        Block backup = reloadedSection.Blocks.Single(b => b.ReplacementTag == "signer_backup");
        Block other = reloadedSection.Blocks.Single(b => b.ReplacementTag == "other");
        foreach (Block grafted in new[] { main, backup })
        {
            Assert.Equal("Who countersigns?", ((AiQueryProperties)grafted.Properties).UserPromptTemplate);
            Assert.Equal(result.Value.HeadContentHash, grafted.SharedComponentVersionHash);
            Assert.Equal(idsBefore[grafted.ReplacementTag], grafted.StableBlockId);
        }

        Assert.Equal(["kyc"], main.SupportedDocumentClasses);
        Assert.Equal(["kyc"], backup.SupportedDocumentClasses);
        Assert.Equal("Untouched", ((AiQueryProperties)other.Properties).UserPromptTemplate);
        Assert.Null(other.SharedComponentId);
    }

    [Fact]
    public async Task Append_IdenticalContent_DedupsAndSkipsFanOut()
    {
        (string definitionJsonV1, string hashV1, SharedComponentVersion v1) = await SeedLinkedSectionComponentAsync();
        int workflowVersionsBefore = await _coreContext.WorkflowVersions
            .CountAsync(TestContext.Current.CancellationToken);

        Section submitted = BuildSectionDefinition();
        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, submitted.ToJson(pretty: false), v1.Id, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.True(result.Value.WasDeduped);
        Assert.Equal(v1.Id, result.Value.HeadVersionId);
        Assert.Equal(hashV1, result.Value.HeadContentHash);
        Assert.Empty(result.Value.ConsumerSaves);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
        Assert.Equal(workflowVersionsBefore, await _coreContext.WorkflowVersions
            .CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Append_PayloadAbsentHead_AppendsRepairVersionInsteadOfDeduping()
    {
        (string definitionJson, string hash) = Definition(BuildSectionDefinition());
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(
            COMPONENT_ID, 1, hash, contentJson: null, payloadAbsentAt: DateTime.UtcNow);

        Section submitted = BuildSectionDefinition();
        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, submitted.ToJson(pretty: false), v1.Id, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.False(result.Value.WasDeduped);
        Assert.Empty(result.Value.ConsumerSaves);
        SharedComponentVersion v2 = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(v => v.SequenceNumber == 2, TestContext.Current.CancellationToken);
        Assert.Equal(hash, v2.ContentHash);
        Assert.Equal(definitionJson, v2.ContentJson);
        Assert.Null(v2.PayloadAbsentAt);
        Assert.Equal(v2.Id, result.Value.HeadVersionId);
    }

    [Fact]
    public async Task Append_StaleExpectedHead_ConflictsWithHeadMetadata()
    {
        (string definitionJsonV1, string hashV1) = Definition(BuildSectionDefinition());
        (string definitionJsonV2, string hashV2) = Definition(BuildSectionDefinition("Extract the spot FX rate"));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, definitionJsonV1);
        SharedComponentVersion v2 = await _harness.AddVersionAsync(COMPONENT_ID, 2, hashV2, definitionJsonV2, parentVersionId: v1.Id);

        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, BuildSectionDefinition("Another change").ToJson(pretty: false), v1.Id,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.HEAD_MOVED, result.FirstError.Code);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(v2.Id, result.FirstError.Metadata!["currentHeadVersionId"]);
        Assert.Equal(hashV2, result.FirstError.Metadata["currentContentHash"]);
        Assert.Equal(2, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Append_UnknownComponent_NotFound()
    {
        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, "missing", BuildSectionDefinition().ToJson(pretty: false), "v-any",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Append_RetiredComponent_Conflicts()
    {
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", SharedComponentState.Retired, id: COMPONENT_ID);

        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, BuildSectionDefinition().ToJson(pretty: false), "v-any",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.RETIRED, result.FirstError.Code);
    }

    [Fact]
    public async Task Append_ZeroVersions_Conflicts()
    {
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);

        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, BuildSectionDefinition().ToJson(pretty: false), "v-any",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.NO_VERSIONS, result.FirstError.Code);
    }

    [Fact]
    public async Task Append_UnparseableDefinition_ValidationError()
    {
        (string definitionJson, string hash) = Definition(BuildSectionDefinition());
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, definitionJson);

        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, "not json {{", "v-any", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.CORRUPT_DEFINITION, result.FirstError.Description);
    }

    [Fact]
    public async Task Append_DuplicateOrEmptyInnerTags_ValidationError()
    {
        (string definitionJson, string hash) = Definition(BuildSectionDefinition());
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, definitionJson);

        var duplicates = new Workflow("Scratch");
        var duplicateSection = new Section("Dup", duplicates) { Title = "Dup" };
        _ = new Block(BlockTypes.AiQuery, duplicateSection, "rate") { Name = "One" };
        _ = new Block(BlockTypes.AiQuery, duplicateSection, "rate") { Name = "Two" };
        ErrorOr<SharedComponentAppendResult> duplicateResult = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, duplicateSection.ToJson(pretty: false), "v-any", TestContext.Current.CancellationToken);

        Assert.True(duplicateResult.IsError);
        Assert.Equal(ErrorType.Validation, duplicateResult.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, duplicateResult.FirstError.Description);

        var empties = new Workflow("Scratch");
        var emptyTagSection = new Section("Empty", empties) { Title = "Empty" };
        _ = new Block(BlockTypes.AiQuery, emptyTagSection, string.Empty) { Name = "Untagged" };
        ErrorOr<SharedComponentAppendResult> emptyResult = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, emptyTagSection.ToJson(pretty: false), "v-any", TestContext.Current.CancellationToken);

        Assert.True(emptyResult.IsError);
        Assert.Contains(SharedComponentErrorCodes.INVALID_LINK_SHAPE, emptyResult.FirstError.Description);
    }

    [Fact]
    public async Task Append_OrphaningRemovedSlot_RequiresAcknowledgementThenDeletes()
    {
        (string definitionJsonV1, string hashV1, SharedComponentVersion v1) =
            await SeedLinkedSectionComponentAsync(declareSourceSignatureFields: true);
        string sourceStableId = (await LinkedSectionStableIdsByTagAsync("WT-A"))["source"];
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-client",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "director_name",
            Value = "Jane Director",
        });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var scratch = new Workflow("Scratch");
        var slim = new Section("Market Data", scratch) { Title = "Market Data" };
        _ = new Block(BlockTypes.AiQuery, slim, "rate")
        {
            Name = "Rate",
            Properties = new AiQueryProperties { UserPromptTemplate = "Extract the FX rate" },
        };

        ErrorOr<SharedComponentAppendResult> refused = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, slim.ToJson(pretty: false), v1.Id, TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(ErrorType.Conflict, refused.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT, refused.FirstError.Code);
        var orphans = Assert.IsAssignableFrom<IReadOnlyList<SharedComponentOverrideImpact>>(
            refused.FirstError.Metadata!["orphanedOverrides"]);
        SharedComponentOverrideImpact orphan = Assert.Single(orphans);
        Assert.Equal("director_name", orphan.PlaceholderName);
        Assert.Equal(SharedComponentOverrideImpactKind.Deleted, orphan.Kind);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));

        ErrorOr<SharedComponentAppendResult> accepted = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, slim.ToJson(pretty: false), v1.Id,
            acknowledgeSignatureOrphans: true, TestContext.Current.CancellationToken);

        Assert.False(accepted.IsError);
        SharedComponentConsumerSaveResult save = Assert.Single(accepted.Value.ConsumerSaves);
        SharedComponentOverrideImpact reported = Assert.Single(save.OrphanedOverrides);
        Assert.Equal("director_name", reported.PlaceholderName);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Section linked = reloaded.Sections.Single(s => s.SharedComponentId == COMPONENT_ID);
        Assert.DoesNotContain(linked.Blocks, b => b.ReplacementTag == "source");
        Assert.Equal(0, await _coreContext.SignatureFieldOverrides
            .CountAsync(o => o.WorkflowTypeId == "WT-A", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Append_SectionShapedJsonForBlockComponent_RejectsKindMismatch()
    {
        (string definitionJson, string hash) = Definition(BuildBlockDefinition("Who signs?"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer Check", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, definitionJson);

        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, BuildSectionDefinition().ToJson(pretty: false), v1.Id,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.KIND_MISMATCH, result.FirstError.Description);
    }

    [Fact]
    public async Task Append_BlockShapedJsonForSectionComponent_RejectsKindMismatch()
    {
        (string definitionJson, string hash) = Definition(BuildSectionDefinition());
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, definitionJson);

        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, BuildBlockDefinition("Who signs?").ToJson(pretty: false), v1.Id,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Contains(SharedComponentErrorCodes.KIND_MISMATCH, result.FirstError.Description);
    }

    [Fact]
    public async Task Append_ConsumerSaveThrows_DetachesPendingVersion()
    {
        (string definitionJsonV1, string hashV1, SharedComponentVersion v1) = await SeedLinkedSectionComponentAsync();
        SharedComponentFanOutService throwingService = _harness.CreateFanOutService(
            new ThrowingWorkflowSavePipeline(_harness, () => new InvalidOperationException("boom")));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => throwingService.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, BuildSectionDefinition("Changed").ToJson(pretty: false), v1.Id,
            TestContext.Current.CancellationToken));

        Assert.DoesNotContain(
            _coreContext.ChangeTracker.Entries<SharedComponentVersion>(),
            e => e.State == EntityState.Added);
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Append_ConsumerSaveRaces_ConflictsWithConcurrentSave()
    {
        (string definitionJsonV1, string hashV1, SharedComponentVersion v1) = await SeedLinkedSectionComponentAsync();
        SharedComponentFanOutService racingService = _harness.CreateFanOutService(
            new ThrowingWorkflowSavePipeline(_harness, () => new DbUpdateConcurrencyException("raced")));

        ErrorOr<SharedComponentAppendResult> result = await racingService.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, BuildSectionDefinition("Changed").ToJson(pretty: false), v1.Id,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(WorkflowSavePipeline.CONCURRENT_SAVE_CODE, result.FirstError.Code);
        Assert.Equal(1, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(
            _coreContext.ChangeTracker.Entries<SharedComponentVersion>(),
            e => e.State == EntityState.Added);
    }

    [Fact]
    public async Task Append_RivalAppendWinsRace_ConflictsWithConcurrentAppendMetadata()
    {
        (string definitionJsonV1, string hashV1, SharedComponentVersion v1) = await SeedLinkedSectionComponentAsync();
        string rivalHash = new('c', 64);
        string rivalVersionId = string.Empty;
        SharedComponentFanOutService racedService = _harness.CreateFanOutService(new ThrowingWorkflowSavePipeline(_harness, () =>
        {
            using var rivalContext = new CoreDbContext(_harness.CoreOptions);
            var rivalVersion = new SharedComponentVersion
            {
                SharedComponentId = COMPONENT_ID,
                SequenceNumber = 2,
                ContentHash = rivalHash,
                ParentVersionId = v1.Id,
                CreatedByUserId = "user-rival",
                ContentJson = "{}",
            };
            _ = rivalContext.Add(rivalVersion);
            _ = rivalContext.SaveChanges();
            rivalVersionId = rivalVersion.Id;

            EntityEntry<SharedComponentVersion> pending = _coreContext.ChangeTracker
                .Entries<SharedComponentVersion>()
                .Single(e => e.State == EntityState.Added);
#pragma warning disable EF1001 // Internal EF Core API usage.
            return new DbUpdateException(
                "duplicate key value violates unique index",
                [(IUpdateEntry)pending.GetInfrastructure()]);
#pragma warning restore EF1001 // Internal EF Core API usage.
        }));

        ErrorOr<SharedComponentAppendResult> result = await racedService.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, BuildSectionDefinition("Changed").ToJson(pretty: false), v1.Id,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(SharedComponentErrorCodes.CONCURRENT_APPEND, result.FirstError.Code);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(rivalVersionId, result.FirstError.Metadata!["currentHeadVersionId"]);
        Assert.Equal(rivalHash, result.FirstError.Metadata["currentContentHash"]);
        List<SharedComponentVersion> versions = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .Where(v => v.SharedComponentId == COMPONENT_ID)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);
        Assert.DoesNotContain(versions, v => v.CreatedByUserId == USER);
        Assert.DoesNotContain(
            _coreContext.ChangeTracker.Entries<SharedComponentVersion>(),
            e => e.State == EntityState.Added);
    }

    [Fact]
    public async Task Append_ConsumerRefEntryHoldsLocalEdits_RefusesUntilAcknowledged()
    {
        (SharedComponentVersion head, string refHeadHash) = await SeedRefBearingSectionComponentAsync();
        Workflow consumer = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Block liveRef = consumer.Sections
            .Single(section => section.SharedComponentId == COMPONENT_ID)
            .Blocks
            .Single(block => block.ReplacementTag == "source");
        ((AiQueryProperties)liveRef.Properties).UserPromptTemplate = "Cite the source, and name the analyst";
        _coreContext.ChangeTracker.Clear();
        ErrorOr<SaveWorkflowResult> localEdit = await _saveHandler.Handle(
            new SaveWorkflowCommand(ORG, "user-seed", "WT-A", consumer),
            TestContext.Current.CancellationToken);
        Assert.False(localEdit.IsError);
        _coreContext.ChangeTracker.Clear();

        Section revised = BuildRefBearingSectionDefinition("Extract the FX rate for the reporting date");
        int versionsBefore = await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentAppendResult> refused = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, revised.ToJson(pretty: false), head.Id, TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(ErrorType.Conflict, refused.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.OVERWRITES_LOCAL_ENTRIES, refused.FirstError.Code);
        Assert.Equal("WT-A", refused.FirstError.Metadata!["workflowTypeId"]);
        var slots = Assert.IsAssignableFrom<IReadOnlyList<string>>(refused.FirstError.Metadata!["overwrittenEntrySlots"]);
        Assert.Equal(["source"], slots);
        Assert.Equal(versionsBefore, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));

        ErrorOr<SharedComponentAppendResult> accepted = await _service.AppendAndFanOutAsync(
            ORG,
            USER,
            COMPONENT_ID,
            revised.ToJson(pretty: false),
            head.Id,
            acknowledgeSignatureOrphans: false,
            TestContext.Current.CancellationToken,
            pendingRefTarget: null,
            acknowledgeOverwrittenEntries: true);

        Assert.False(accepted.IsError);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Block regrafted = reloaded.Sections
            .Single(section => section.SharedComponentId == COMPONENT_ID)
            .Blocks
            .Single(block => block.ReplacementTag == "source");
        Assert.Equal("Cite the source", ((AiQueryProperties)regrafted.Properties).UserPromptTemplate);
        Assert.Equal(refHeadHash, regrafted.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Append_ConsumerRefEntryUntouched_IsNotRefused()
    {
        (SharedComponentVersion head, _) = await SeedRefBearingSectionComponentAsync();

        Section revised = BuildRefBearingSectionDefinition("Extract the FX rate for the reporting date");

        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, revised.ToJson(pretty: false), head.Id, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task Append_ConsumerInlineEntryHoldsLocalEdits_RefusesUntilAcknowledged()
    {
        (_, _, SharedComponentVersion v1) = await SeedLinkedSectionComponentAsync();
        Workflow consumer = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Block liveRate = consumer.Sections
            .Single(section => section.SharedComponentId == COMPONENT_ID)
            .Blocks
            .Single(block => block.ReplacementTag == "rate");
        ((AiQueryProperties)liveRate.Properties).UserPromptTemplate = "LOCAL EDIT do not lose me";
        _coreContext.ChangeTracker.Clear();
        ErrorOr<SaveWorkflowResult> localEdit = await _saveHandler.Handle(
            new SaveWorkflowCommand(ORG, "user-seed", "WT-A", consumer),
            TestContext.Current.CancellationToken);
        Assert.False(localEdit.IsError);
        _coreContext.ChangeTracker.Clear();

        Section revised = BuildSectionDefinition("HEAD v2");
        int versionsBefore = await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentAppendResult> refused = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, revised.ToJson(pretty: false), v1.Id, TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(ErrorType.Conflict, refused.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.OVERWRITES_LOCAL_ENTRIES, refused.FirstError.Code);
        Assert.Equal("WT-A", refused.FirstError.Metadata!["workflowTypeId"]);
        var slots = Assert.IsAssignableFrom<IReadOnlyList<string>>(refused.FirstError.Metadata!["overwrittenEntrySlots"]);
        Assert.Equal(["rate"], slots);
        Assert.Equal(versionsBefore, await _coreContext.SharedComponentVersions
            .CountAsync(v => v.SharedComponentId == COMPONENT_ID, TestContext.Current.CancellationToken));
        Workflow untouched = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Block untouchedRate = untouched.Sections
            .Single(section => section.SharedComponentId == COMPONENT_ID)
            .Blocks
            .Single(block => block.ReplacementTag == "rate");
        Assert.Equal("LOCAL EDIT do not lose me", ((AiQueryProperties)untouchedRate.Properties).UserPromptTemplate);

        ErrorOr<SharedComponentAppendResult> accepted = await _service.AppendAndFanOutAsync(
            ORG,
            USER,
            COMPONENT_ID,
            revised.ToJson(pretty: false),
            v1.Id,
            acknowledgeSignatureOrphans: false,
            TestContext.Current.CancellationToken,
            pendingRefTarget: null,
            acknowledgeOverwrittenEntries: true);

        Assert.False(accepted.IsError);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Section regrafted = reloaded.Sections.Single(section => section.SharedComponentId == COMPONENT_ID);
        Assert.Equal("HEAD v2", ((AiQueryProperties)regrafted.Blocks
            .Single(block => block.ReplacementTag == "rate").Properties).UserPromptTemplate);
        Assert.Equal(accepted.Value.HeadContentHash, regrafted.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Append_BlockKindConsumerHoldsLocalEdits_RefusesUntilAcknowledged()
    {
        Block defV1 = BuildBlockDefinition("Check the ID");
        (string defJsonV1, string hashV1) = Definition(defV1);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "KYC", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, defJsonV1);

        var consumer = new Workflow("Consumer") { Title = "Consumer", WorkflowTypeId = "WT-BLK" };
        var body = new Section("Body", consumer) { Title = "Body" };
        _ = LinkedBlock(body, "kyc", "Check the ID", hashV1);
        _ = await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-BLK", consumer);

        Workflow edited = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-BLK"))!;
        Block liveInstance = edited.GetAllBlocks().Single(b => b.SharedComponentId == COMPONENT_ID);
        ((AiQueryProperties)liveInstance.Properties).UserPromptTemplate = "LOCAL EDIT do not lose me";
        _coreContext.ChangeTracker.Clear();
        ErrorOr<SaveWorkflowResult> localEdit = await _saveHandler.Handle(
            new SaveWorkflowCommand(ORG, "user-seed", "WT-BLK", edited),
            TestContext.Current.CancellationToken);
        Assert.False(localEdit.IsError);
        _coreContext.ChangeTracker.Clear();

        Block defV2 = BuildBlockDefinition("Check the ID twice");

        ErrorOr<SharedComponentAppendResult> refused = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, defV2.ToJson(pretty: false), v1.Id, TestContext.Current.CancellationToken);

        Assert.True(refused.IsError);
        Assert.Equal(ErrorType.Conflict, refused.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.OVERWRITES_LOCAL_ENTRIES, refused.FirstError.Code);
        Assert.Equal("WT-BLK", refused.FirstError.Metadata!["workflowTypeId"]);
        var slots = Assert.IsAssignableFrom<IReadOnlyList<string>>(refused.FirstError.Metadata!["overwrittenEntrySlots"]);
        Assert.Equal(["kyc"], slots);
        Workflow untouched = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-BLK"))!;
        Assert.Equal(
            "LOCAL EDIT do not lose me",
            ((AiQueryProperties)untouched.GetAllBlocks()
                .Single(b => b.SharedComponentId == COMPONENT_ID).Properties).UserPromptTemplate);

        ErrorOr<SharedComponentAppendResult> accepted = await _service.AppendAndFanOutAsync(
            ORG,
            USER,
            COMPONENT_ID,
            defV2.ToJson(pretty: false),
            v1.Id,
            acknowledgeSignatureOrphans: false,
            TestContext.Current.CancellationToken,
            pendingRefTarget: null,
            acknowledgeOverwrittenEntries: true);

        Assert.False(accepted.IsError);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-BLK"))!;
        Block regrafted = reloaded.GetAllBlocks().Single(b => b.SharedComponentId == COMPONENT_ID);
        Assert.Equal("Check the ID twice", ((AiQueryProperties)regrafted.Properties).UserPromptTemplate);
        Assert.Equal(accepted.Value.HeadContentHash, regrafted.SharedComponentVersionHash);
    }

    [Fact]
    public async Task Append_ConsumerWithHoistedSignatureFields_IsNotRefused()
    {
        (_, _, SharedComponentVersion v1) = await SeedLinkedSectionComponentAsync(declareSourceSignatureFields: true);

        Section revised = BuildSectionDefinition("HEAD v2");
        DeclareDirectorSignature(revised);

        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, revised.ToJson(pretty: false), v1.Id, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Section regrafted = reloaded.Sections.Single(section => section.SharedComponentId == COMPONENT_ID);
        Assert.Equal("HEAD v2", ((AiQueryProperties)regrafted.Blocks
            .Single(block => block.ReplacementTag == "rate").Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Append_ConsumerHoldsOnlyLocalAdditions_IsNotRefused()
    {
        (_, _, SharedComponentVersion v1) = await SeedLinkedSectionComponentAsync();
        Workflow consumer = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Section linked = consumer.Sections.Single(section => section.SharedComponentId == COMPONENT_ID);
        _ = new Block(BlockTypes.AiQuery, linked, "note")
        {
            Name = "Note",
            Properties = new AiQueryProperties { UserPromptTemplate = "Purely local addition" },
        };
        _coreContext.ChangeTracker.Clear();
        ErrorOr<SaveWorkflowResult> localAdd = await _saveHandler.Handle(
            new SaveWorkflowCommand(ORG, "user-seed", "WT-A", consumer),
            TestContext.Current.CancellationToken);
        Assert.False(localAdd.IsError);
        _coreContext.ChangeTracker.Clear();

        Section revised = BuildSectionDefinition("HEAD v2");

        ErrorOr<SharedComponentAppendResult> result = await _service.AppendAndFanOutAsync(
            ORG, USER, COMPONENT_ID, revised.ToJson(pretty: false), v1.Id, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-A"))!;
        Section regrafted = reloaded.Sections.Single(section => section.SharedComponentId == COMPONENT_ID);
        Assert.Equal("HEAD v2", ((AiQueryProperties)regrafted.Blocks
            .Single(block => block.ReplacementTag == "rate").Properties).UserPromptTemplate);
        Assert.Equal(
            "Purely local addition",
            ((AiQueryProperties)regrafted.Blocks
                .Single(block => block.ReplacementTag == "note").Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Append_ConsumerCarryingAgentBlock_DisabledRuntime_RefusesNamingTheConsumer()
    {
        AgentRuntimeSettings enabled = AgentTestData.EnabledRuntime();
        (_, SaveWorkflowCommandHandler seedHandler) = _harness.CreateRealSaveSender(enabled);
        (string defJsonV1, string hashV1) = Definition(BuildBlockDefinition("Check the signer"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, defJsonV1);

        Workflow consumer = BuildAgentCarryingConsumer(hashV1);
        _ = await _harness.SaveTemplateAsync(seedHandler, ORG, "user-seed", "WT-AGENT", consumer);

        SharedComponentFanOutService pushService = _harness.CreateFanOutService(
            new AgentRuntimeSettings { Enabled = false });
        ErrorOr<SharedComponentAppendResult> result = await pushService.AppendAndFanOutAsync(
            ORG,
            USER,
            COMPONENT_ID,
            BuildBlockDefinition("Check the signer twice").ToJson(pretty: false),
            v1.Id,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e =>
            e.Description.Contains("WT-AGENT", StringComparison.Ordinal)
            && e.Description.Contains("agent runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Append_ConsumerCarryingAgentBlock_EnabledRuntime_FansOut()
    {
        AgentRuntimeSettings enabled = AgentTestData.EnabledRuntime();
        (_, SaveWorkflowCommandHandler seedHandler) = _harness.CreateRealSaveSender(enabled);
        (string defJsonV1, string hashV1) = Definition(BuildBlockDefinition("Check the signer"));
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, defJsonV1);

        Workflow consumer = BuildAgentCarryingConsumer(hashV1);
        _ = await _harness.SaveTemplateAsync(seedHandler, ORG, "user-seed", "WT-AGENT", consumer);

        SharedComponentFanOutService pushService = _harness.CreateFanOutService(enabled);
        ErrorOr<SharedComponentAppendResult> result = await pushService.AppendAndFanOutAsync(
            ORG,
            USER,
            COMPONENT_ID,
            BuildBlockDefinition("Check the signer twice").ToJson(pretty: false),
            v1.Id,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.ConsumerSaves);
        Assert.Equal("WT-AGENT", save.WorkflowTypeId);
        Workflow reloaded = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("WT-AGENT"))!;
        Block after = reloaded.GetAllBlocks().Single(b => b.SharedComponentId == COMPONENT_ID);
        Assert.Equal("Check the signer twice", ((AiQueryProperties)after.Properties).UserPromptTemplate);
    }

    [Fact]
    public async Task Append_AgentSkillGainsUnknownTag_ConsumerWarningNamesIt()
    {
        AgentRuntimeSettings enabled = AgentTestData.EnabledRuntime();
        (_, SaveWorkflowCommandHandler seedHandler) = _harness.CreateRealSaveSender(enabled);
        (string defJsonV1, string hashV1) = Definition(AgentTestData.AgentBlockDefinition());
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Extractor", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, defJsonV1);

        var consumer = new Workflow("AgentConsumer") { Title = "Agent consumer" };
        var body = new Section("Body", consumer) { Title = "Body" };
        Block instance = AgentTestData.AddAgent(body, "extractor");
        instance.SharedComponentId = COMPONENT_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = hashV1;
        _ = await _harness.SaveTemplateAsync(seedHandler, ORG, "user-seed", "WT-AGENT", consumer);

        SharedComponentFanOutService pushService = _harness.CreateFanOutService(enabled);
        ErrorOr<SharedComponentAppendResult> result = await pushService.AppendAndFanOutAsync(
            ORG,
            USER,
            COMPONENT_ID,
            AgentTestData.AgentBlockDefinition("Read {{ghost_tag.answer}} first.").ToJson(pretty: false),
            v1.Id,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentConsumerSaveResult save = Assert.Single(result.Value.ConsumerSaves);
        Assert.Contains(save.Warnings, w => w.Message.Contains("ghost_tag", StringComparison.Ordinal));
    }

    private async Task<(SharedComponentVersion Head, string RefHeadHash)> SeedRefBearingSectionComponentAsync()
    {
        Block refDefinition = BuildBlockDefinition("Cite the source");
        (string refJson, string refHash) = Definition(refDefinition);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Source", id: REF_COMPONENT_ID);
        _ = await _harness.AddVersionAsync(REF_COMPONENT_ID, 1, refHash, refJson);

        (string sectionJson, string sectionHash) = Definition(BuildRefBearingSectionDefinition());
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion seeded = await _harness.AddVersionAsync(COMPONENT_ID, 1, sectionHash, sectionJson);

        var workflow = new Workflow("Consumer") { Title = "Consumer" };
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
        linked.SharedComponentId = COMPONENT_ID;
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentVersionHash = sectionHash;
        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", workflow);

        ErrorOr<SharedComponentAppendResult> hydrate = await _service.AppendAndFanOutAsync(
            ORG,
            USER,
            COMPONENT_ID,
            BuildRefBearingSectionDefinition("Extract the FX rate today").ToJson(pretty: false),
            seeded.Id,
            TestContext.Current.CancellationToken);
        Assert.False(hydrate.IsError);

        SharedComponentVersion head = await _coreContext.SharedComponentVersions
            .AsNoTracking()
            .SingleAsync(v => v.Id == hydrate.Value.HeadVersionId, TestContext.Current.CancellationToken);

        return (head, refHash);
    }

    private static Section BuildRefBearingSectionDefinition(string ratePrompt = "Extract the FX rate")
    {
        var scratch = new Workflow("Scratch");
        var section = new Section("Market Data", scratch) { Title = "Market Data" };
        _ = new Block(BlockTypes.AiQuery, section, "rate")
        {
            Name = "Rate",
            Properties = new AiQueryProperties { UserPromptTemplate = ratePrompt },
        };
        section.Entries =
        [
            new SectionEntry { SlotTag = "rate", Kind = SectionEntryKind.Inline },
            new SectionEntry
            {
                SlotTag = "source",
                Kind = SectionEntryKind.ComponentRef,
                SharedComponentId = REF_COMPONENT_ID,
            },
        ];

        return section;
    }

    private async Task<(string DefinitionJson, string Hash, SharedComponentVersion Head)> SeedLinkedSectionComponentAsync(
        bool declareSourceSignatureFields = false)
    {
        Section definitionSection = BuildSectionDefinition();
        if (declareSourceSignatureFields)
        {
            DeclareDirectorSignature(definitionSection);
        }

        (string definitionJsonV1, string hashV1) = Definition(definitionSection);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion v1 = await _harness.AddVersionAsync(COMPONENT_ID, 1, hashV1, definitionJsonV1);
        Workflow consumer = BuildLinkedConsumer(hashV1);
        if (declareSourceSignatureFields)
        {
            DeclareDirectorSignature(consumer.Sections.Single(s => s.SharedComponentId == COMPONENT_ID));
        }

        await _harness.SaveTemplateAsync(_saveHandler, ORG, "user-seed", "WT-A", consumer);

        return (definitionJsonV1, hashV1, v1);
    }

    private static void DeclareDirectorSignature(Section section)
    {
        Block source = section.Blocks.Single(b => b.ReplacementTag == "source");
        ((AiQueryProperties)source.Properties).SignatureFields = new Dictionary<string, SignatureFieldDefinition>
        {
            ["director_name"] = new("Director", "Who signs", "N/A"),
        };
    }

    private (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        return _harness.Definition(section);
    }

    private (string DefinitionJson, string ContentHash) Definition(Block block)
    {
        return _harness.Definition(block);
    }

    private static Section BuildSectionDefinition(
        string ratePrompt = "Extract the FX rate",
        bool includeCurrency = false)
    {
        var scratch = new Workflow("Scratch");
        var section = new Section("Market Data", scratch) { Title = "Market Data" };
        _ = new Block(BlockTypes.AiQuery, section, "rate")
        {
            Name = "Rate",
            Properties = new AiQueryProperties { UserPromptTemplate = ratePrompt },
        };
        _ = new Block(BlockTypes.AiQuery, section, "source")
        {
            Name = "Source",
            Properties = new AiQueryProperties { UserPromptTemplate = "Cite the source" },
        };
        if (includeCurrency)
        {
            _ = new Block(BlockTypes.AiQuery, section, "currency")
            {
                Name = "Currency",
                Properties = new AiQueryProperties { UserPromptTemplate = "Name the currency" },
            };
        }

        return section;
    }

    private static Workflow BuildAgentCarryingConsumer(string versionHash)
    {
        var workflow = new Workflow("AgentConsumer") { Title = "Agent consumer" };
        var body = new Section("Body", workflow) { Title = "Body" };
        _ = LinkedBlock(body, "signer", "Check the signer", versionHash);
        var agentSection = new Section("Agents", workflow) { Title = "Agents" };
        _ = AgentTestData.AddAgent(agentSection, "extraction_agent");

        return workflow;
    }

    private static Block BuildBlockDefinition(string prompt)
    {
        var scratch = new Workflow("Scratch");
        var section = new Section("Scratch", scratch) { Title = "Scratch" };

        return new Block(BlockTypes.AiQuery, section, "definition")
        {
            Name = "Signer",
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    private static Workflow BuildLinkedConsumer(
        string versionHash,
        string instanceRateTag = "rate",
        Dictionary<string, string>? bindings = null)
    {
        var workflow = new Workflow("Consumer") { Title = "Consumer" };
        var intro = new Section("Intro", workflow) { Title = "Intro" };
        _ = new Block(BlockTypes.AiQuery, intro, "intro_note")
        {
            Name = "Note",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarize" },
        };
        var linked = new Section("Market Data", workflow) { Title = "Market Data" };
        _ = new Block(BlockTypes.AiQuery, linked, instanceRateTag)
        {
            Name = "Rate",
            Properties = new AiQueryProperties { UserPromptTemplate = "Extract the FX rate" },
        };
        _ = new Block(BlockTypes.AiQuery, linked, "source")
        {
            Name = "Source",
            Properties = new AiQueryProperties { UserPromptTemplate = "Cite the source" },
        };
        linked.SharedComponentId = COMPONENT_ID;
        linked.SharedComponentLinkState = SharedComponentLinkState.Linked;
        linked.SharedComponentVersionHash = versionHash;
        linked.SharedComponentBindings = bindings;

        return workflow;
    }

    private static Block LinkedBlock(Section section, string tag, string prompt, string versionHash)
    {
        var block = new Block(BlockTypes.AiQuery, section, tag)
        {
            Name = tag,
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
        block.SharedComponentId = COMPONENT_ID;
        block.SharedComponentLinkState = SharedComponentLinkState.Linked;
        block.SharedComponentVersionHash = versionHash;

        return block;
    }

    private async Task<Dictionary<string, string>> LinkedSectionStableIdsByTagAsync(string workflowTypeId)
    {
        Workflow workflow = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId))!;

        return workflow.Sections
            .Single(s => s.SharedComponentId == COMPONENT_ID)
            .Blocks
            .ToDictionary(b => b.ReplacementTag, b => b.StableBlockId!, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, string>> AllStableIdsByTagAsync(string workflowTypeId)
    {
        Workflow workflow = (await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId))!;

        return workflow.GetAllBlocks()
            .ToDictionary(b => b.ReplacementTag, b => b.StableBlockId!, StringComparer.Ordinal);
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
