using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.PreviewSharedComponentReconcile;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Testing;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using static IniaAi.Application.UnitTests.Common.Workflows.SharedComponentLinkTestData;

namespace IniaAi.Application.UnitTests.Queries.SharedComponents;

public class PreviewSharedComponentReconcileQueryHandlerTests : IDisposable
{
    private const string ORG = "org-test";
    private const string USER = "user-test";
    private const string COMPONENT_ID = "component-1";

    private readonly SharedComponentsTestHarness _harness = new();
    private readonly CoreDbContext _coreContext;
    private readonly UserDataDbContext _userDataContext;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly PreviewSharedComponentReconcileQueryHandler _handler;

    public PreviewSharedComponentReconcileQueryHandlerTests()
    {
        _coreContext = _harness.Context;
        _userDataContext = _harness.UserDataContext;
        _coreUnitOfWork = _harness.UnitOfWork;
        _handler = new PreviewSharedComponentReconcileQueryHandler(_coreUnitOfWork, _harness.CreateFanOutService());
    }

    [Fact]
    public async Task Preview_NoCandidate_ReturnsConsumers_AndNoBaselineWithoutValidation()
    {
        (string defJson, string hash) = Definition(DefSection("Market Data", ("rate", "Extract")));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        SharedComponentVersion head = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, defJson);
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-B", "Beta Type"));
        _ = _coreContext.Add(BuildLinkedConsumer("WT-A", hash));
        _ = _coreContext.Add(BuildLinkedConsumer("WT-B", hash));
        string freshHash = new('1', 64);
        string movedHash = new('2', 64);
        _ = _coreContext.Add(_harness.BuildWorkflowVersion("WT-A", freshHash, createdByUserId: "user-seed"));
        _ = _coreContext.Add(_harness.BuildWorkflowVersion("WT-B", movedHash, createdByUserId: "user-seed"));
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _ = _userDataContext.Add(_harness.BuildRegistration("WT-A", "org-1", freshHash, markedByUserId: "user-seed"));
        _ = _userDataContext.Add(
            _harness.BuildRegistration("WT-A", "org-2", new string('0', 64), markedByUserId: "user-seed"));
        _ = _userDataContext.Add(_harness.BuildRegistration(
            "WT-B", "org-3", movedHash, BaselineRegistrationState.Superseded, markedByUserId: "user-seed"));
        _ = await _userDataContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        ErrorOr<SharedComponentReconcilePreviewResult> result = await _handler.Handle(
            new PreviewSharedComponentReconcileQuery(ORG, USER, COMPONENT_ID, null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(COMPONENT_ID, result.Value.SharedComponentId);
        Assert.Equal(head.Id, result.Value.HeadVersionId);
        Assert.Equal(hash, result.Value.HeadContentHash);
        Assert.Null(result.Value.CandidateContentHash);
        Assert.Equal(2, result.Value.Consumers.Count);
        Assert.Contains(result.Value.Consumers, c => c.WorkflowTypeId == "WT-A");
        Assert.Contains(result.Value.Consumers, c => c.WorkflowTypeId == "WT-B");
        Assert.Null(result.Value.UpdateSharedImpact.SlotDelta);
        Assert.Empty(result.Value.UpdateSharedImpact.OrphanedSignatureOverrides);
        Assert.Empty(result.Value.UpdateSharedImpact.AffectedBaselines);
    }

    [Fact]
    public async Task Preview_SectionCandidate_ComputesSlotDelta()
    {
        Section oldDef = DefSection(
            "Market Data",
            ("rate", "Extract the FX rate"),
            ("source", "Cite the source"),
            ("legacy", "Legacy extraction"));
        oldDef.Blocks.Single(b => b.ReplacementTag == "legacy").Name = "Legacy Extraction";
        (string defJson, string hash) = Definition(oldDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, defJson);
        Section candidate = DefSection(
            "Market Data",
            ("rate", "Extract the SPOT rate"),
            ("fx_source", "Cite the source"),
            ("fresh", "Brand new extraction"));
        candidate.Blocks.Single(b => b.ReplacementTag == "fresh").Name = "Fresh Extraction";
        Section candidateTwin = DefSection(
            "Market Data",
            ("rate", "Extract the SPOT rate"),
            ("fx_source", "Cite the source"),
            ("fresh", "Brand new extraction"));
        candidateTwin.Blocks.Single(b => b.ReplacementTag == "fresh").Name = "Fresh Extraction";
        (_, string expectedCandidateHash) = Definition(candidateTwin);

        ErrorOr<SharedComponentReconcilePreviewResult> result = await _handler.Handle(
            new PreviewSharedComponentReconcileQuery(ORG, USER, COMPONENT_ID, candidate.ToJson(pretty: false)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(expectedCandidateHash, result.Value.CandidateContentHash);
        SharedComponentSlotDelta delta = Assert.IsType<SharedComponentSlotDelta>(
            result.Value.UpdateSharedImpact.SlotDelta);
        Assert.Equal(new[] { "rate" }, delta.MatchedSlotTags);
        Assert.Equal(new[] { "fresh" }, delta.AddedSlotTags);
        Assert.Equal(new[] { "legacy" }, delta.RemovedSlotTags);
        KeyValuePair<string, string> rename = Assert.Single(delta.RenamedSlots);
        Assert.Equal("source", rename.Key);
        Assert.Equal("fx_source", rename.Value);
        Assert.Empty(delta.RescuedSlots);
        Assert.Empty(result.Value.UpdateSharedImpact.OrphanedSignatureOverrides);
    }

    [Fact]
    public async Task Preview_RemovedSlot_ReportsOverrideRowsOnBoundInstanceBlocksAsDeleted()
    {
        Section definition = DefSection(
            "Market Data",
            ("rate", "Extract"),
            ("legacy", "Legacy extraction"));
        ((AiQueryProperties)definition.Blocks.Single(b => b.ReplacementTag == "legacy").Properties!).SignatureFields =
            new Dictionary<string, SignatureFieldDefinition>
            {
                ["director_name"] = new("Director", "Who signs", "N/A"),
                ["fund_name"] = new("Fund", "Which fund", "N/A"),
            };
        (string defJson, string hash) = Definition(definition);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, defJson);
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        var workflow = new Workflow("Alpha") { WorkflowTypeId = "WT-A" };
        Section instance = DefSection("Market Data", workflow, ("rate", "Extract"), ("legacy_2", "Legacy extraction"));
        instance.SharedComponentId = COMPONENT_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = hash;
        instance.SharedComponentBindings = new Dictionary<string, string> { ["legacy"] = "legacy_2" };
        string legacyStableId = instance.Blocks.Single(b => b.ReplacementTag == "legacy_2").StableBlockId!;
        string rateStableId = instance.Blocks.Single(b => b.ReplacementTag == "rate").StableBlockId!;
        _ = _coreContext.Add(workflow);
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-1",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "director_name",
            Value = "Jane Roe",
        });
        _ = _coreContext.Add(new PendingSignatureOverride
        {
            OrganizationId = "org-2",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "fund_name",
            ProposedValue = "Fund X",
            ProposerUserId = USER,
        });
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-1",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "director_name",
            Value = "Keep me",
        });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        Section candidate = DefSection("Market Data", ("rate", "Extract"));

        ErrorOr<SharedComponentReconcilePreviewResult> result = await _handler.Handle(
            new PreviewSharedComponentReconcileQuery(ORG, USER, COMPONENT_ID, candidate.ToJson(pretty: false)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        IReadOnlyList<SharedComponentOverrideImpact> impacts =
            result.Value.UpdateSharedImpact.OrphanedSignatureOverrides;
        Assert.Equal(2, impacts.Count);
        Assert.All(impacts, i => Assert.Equal(SharedComponentOverrideImpactKind.Deleted, i.Kind));
        Assert.All(impacts, i => Assert.Equal("legacy_2", i.BlockTag));
        Assert.All(impacts, i => Assert.Equal(legacyStableId, i.StableBlockId));
        SharedComponentOverrideImpact active = impacts.Single(i => !i.IsPending);
        Assert.Equal("org-1", active.OrganizationId);
        Assert.Equal("director_name", active.PlaceholderName);
        SharedComponentOverrideImpact pending = impacts.Single(i => i.IsPending);
        Assert.Equal("org-2", pending.OrganizationId);
        Assert.Equal("fund_name", pending.PlaceholderName);
    }

    [Fact]
    public async Task Preview_MatchedSlotSignaturePlaceholderRemoved_ReportsGoingInert()
    {
        Section oldDef = DefSection("Market Data", ("rate", "Extract"));
        ((AiQueryProperties)oldDef.Blocks.Single().Properties!).SignatureFields =
            new Dictionary<string, SignatureFieldDefinition>
            {
                ["director_name"] = new("Director", "Who signs", "N/A"),
                ["fund_name"] = new("Fund", "Which fund", "N/A"),
            };
        (string defJson, string hash) = Definition(oldDef);
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, defJson);
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        var workflow = new Workflow("Alpha") { WorkflowTypeId = "WT-A" };
        Section instance = DefSection("Market Data", workflow, ("rate", "Extract"));
        instance.SharedComponentId = COMPONENT_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = hash;
        string rateStableId = instance.Blocks.Single().StableBlockId!;
        _ = _coreContext.Add(workflow);
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-1",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "fund_name",
            Value = "Fund X",
        });
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-1",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "director_name",
            Value = "Jane Roe",
        });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        Section candidate = DefSection("Market Data", ("rate", "Extract"));
        ((AiQueryProperties)candidate.Blocks.Single().Properties!).SignatureFields =
            new Dictionary<string, SignatureFieldDefinition>
            {
                ["director_name"] = new("Director", "Who signs", "N/A"),
            };

        ErrorOr<SharedComponentReconcilePreviewResult> result = await _handler.Handle(
            new PreviewSharedComponentReconcileQuery(ORG, USER, COMPONENT_ID, candidate.ToJson(pretty: false)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SharedComponentOverrideImpact impact = Assert.Single(
            result.Value.UpdateSharedImpact.OrphanedSignatureOverrides);
        Assert.Equal(SharedComponentOverrideImpactKind.GoingInert, impact.Kind);
        Assert.Equal("fund_name", impact.PlaceholderName);
        Assert.Equal("rate", impact.BlockTag);
        Assert.Equal(rateStableId, impact.StableBlockId);
        Assert.False(impact.IsPending);
    }

    [Fact]
    public async Task Preview_BlockKindCandidate_ReportsEachGoingInertRowOnce()
    {
        Block oldDef = BuildBlockDefinition("Sign here");
        ((AiQueryProperties)oldDef.Properties!).SignatureFields =
            new Dictionary<string, SignatureFieldDefinition>
            {
                ["director_name"] = new("Director", "Who signs", "N/A"),
                ["fund_name"] = new("Fund", "Which fund", "N/A"),
            };
        (string defJson, string hash) = Definition(oldDef);
        await _harness.AddComponentAsync(SharedComponentKind.Block, "Signer", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, defJson);
        _ = _coreContext.WorkflowTypes.Add(new WorkflowType("WT-A", "Alpha Type"));
        var workflow = new Workflow("Alpha") { WorkflowTypeId = "WT-A" };
        var section = new Section("Main", workflow) { Title = "Main" };
        Block first = LinkedBlock(section, "b1", "Sign here", COMPONENT_ID, hash);
        _ = LinkedBlock(section, "b2", "Sign here", COMPONENT_ID, hash);
        _ = _coreContext.Add(workflow);
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-1",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "fund_name",
            Value = "Fund X",
        });
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-2",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "fund_name",
            Value = "Fund Y",
        });
        _ = _coreContext.Add(new SignatureFieldOverride
        {
            OrganizationId = "org-1",
            WorkflowTypeId = "WT-A",
            PlaceholderName = "director_name",
            Value = "Jane Roe",
        });
        _ = await _coreContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        Block candidate = BuildBlockDefinition("Sign here");
        ((AiQueryProperties)candidate.Properties!).SignatureFields =
            new Dictionary<string, SignatureFieldDefinition>
            {
                ["director_name"] = new("Director", "Who signs", "N/A"),
            };

        ErrorOr<SharedComponentReconcilePreviewResult> result = await _handler.Handle(
            new PreviewSharedComponentReconcileQuery(ORG, USER, COMPONENT_ID, candidate.ToJson(pretty: false)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Null(result.Value.UpdateSharedImpact.SlotDelta);
        IReadOnlyList<SharedComponentOverrideImpact> impacts =
            result.Value.UpdateSharedImpact.OrphanedSignatureOverrides;
        Assert.Equal(2, impacts.Count);
        Assert.All(impacts, i => Assert.Equal(SharedComponentOverrideImpactKind.GoingInert, i.Kind));
        Assert.All(impacts, i => Assert.Equal("fund_name", i.PlaceholderName));
        Assert.Contains(impacts, i => i.OrganizationId == "org-1");
        Assert.Contains(impacts, i => i.OrganizationId == "org-2");
        Assert.All(impacts, i => Assert.Equal(first.StableBlockId, i.StableBlockId));
    }

    [Fact]
    public async Task Preview_CandidateAgainstPayloadAbsentHead_ReturnsPayloadAbsent()
    {
        (_, string hash) = Definition(DefSection("Market Data", ("rate", "Extract")));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, null, payloadAbsentAt: DateTime.UtcNow);
        Section candidate = DefSection("Market Data", ("rate", "Extract"));

        ErrorOr<SharedComponentReconcilePreviewResult> result = await _handler.Handle(
            new PreviewSharedComponentReconcileQuery(ORG, USER, COMPONENT_ID, candidate.ToJson(pretty: false)),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.PAYLOAD_ABSENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Preview_CorruptCandidate_ReturnsValidationError()
    {
        (string defJson, string hash) = Definition(DefSection("Market Data", ("rate", "Extract")));
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);
        _ = await _harness.AddVersionAsync(COMPONENT_ID, 1, hash, defJson);

        ErrorOr<SharedComponentReconcilePreviewResult> result = await _handler.Handle(
            new PreviewSharedComponentReconcileQuery(ORG, USER, COMPONENT_ID, "{ not json"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("definitionJson", result.FirstError.Code);
        Assert.Contains(SharedComponentErrorCodes.CORRUPT_DEFINITION, result.FirstError.Description);
    }

    [Fact]
    public async Task Preview_UnknownComponent_ReturnsNotFound()
    {
        ErrorOr<SharedComponentReconcilePreviewResult> result = await _handler.Handle(
            new PreviewSharedComponentReconcileQuery(ORG, USER, "missing", null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.UNKNOWN_COMPONENT, result.FirstError.Code);
    }

    [Fact]
    public async Task Preview_ZeroVersions_ReturnsConflict()
    {
        await _harness.AddComponentAsync(SharedComponentKind.Section, "Market Data", id: COMPONENT_ID);

        ErrorOr<SharedComponentReconcilePreviewResult> result = await _handler.Handle(
            new PreviewSharedComponentReconcileQuery(ORG, USER, COMPONENT_ID, null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(SharedComponentErrorCodes.NO_VERSIONS, result.FirstError.Code);
    }

    private (string DefinitionJson, string ContentHash) Definition(Section section)
    {
        return _harness.Definition(section);
    }

    private (string DefinitionJson, string ContentHash) Definition(Block block)
    {
        return _harness.Definition(block);
    }

    private static Block LinkedBlock(
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

    private Workflow BuildLinkedConsumer(string workflowTypeId, string versionHash)
    {
        var workflow = new Workflow($"Consumer {workflowTypeId}") { WorkflowTypeId = workflowTypeId };
        Section instance = DefSection("Market Data", workflow, ("rate", "Extract"));
        instance.SharedComponentId = COMPONENT_ID;
        instance.SharedComponentLinkState = SharedComponentLinkState.Linked;
        instance.SharedComponentVersionHash = versionHash;

        return workflow;
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
