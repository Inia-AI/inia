using ErrorOr;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.Signatures.GetWorkflowSignatures;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ContractsEnums = IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.Application.UnitTests.Queries.Signatures.GetWorkflowSignatures;

public class GetWorkflowSignaturesQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string OTHER_ORG_ID = "org-other";
    private const string USER_ID = "user-test";
    private const string WORKFLOW_TYPE_ID = "WT-TestSignatures";
    private const string FIELD_COGS = "COGS_instructions";
    private const string FIELD_REVENUE = "revenue_instructions";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly IniaAi.Infrastructure.Persistence.UserData.Contexts.UserDataDbContext _userDataContext;
    private readonly GetWorkflowSignaturesQueryHandler _handler;

    public GetWorkflowSignaturesQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        _userDataContext = _serviceProvider
            .GetRequiredService<IniaAi.Infrastructure.Persistence.UserData.Contexts.UserDataDbContext>();

        ICoreUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        IUserDataUnitOfWork userDataUnitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new GetWorkflowSignaturesQueryHandler(unitOfWork, _serviceProvider.GetRequiredService<WorkflowTypeAccess>(), userDataUnitOfWork);
    }

    public void Dispose()
    {
        _context.Dispose();
        _userDataContext.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_HappyPath_ProjectsStaffDefaultsAndClientOverrides()
    {
        await SeedWorkflowWithSignaturesAsync(
            ORG_ID,
            WORKFLOW_TYPE_ID,
            workflowName: "Test Workflow");

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(WORKFLOW_TYPE_ID, result.Value.WorkflowTypeId);
        Assert.Equal("Test Workflow", result.Value.WorkflowName);
        Assert.Single(result.Value.Sections);

        SignatureSectionDto section = result.Value.Sections.First();
        Assert.Single(section.Blocks);

        // The block carries name-only references; the field payload lives once in
        // the canonical top-level Fields list.
        SignatureBlockDto block = section.Blocks.First();
        Assert.Equal([FIELD_COGS, FIELD_REVENUE], block.FieldNames);

        Assert.Equal(2, result.Value.Fields.Count);

        SignatureFieldDto defaulted = result.Value.Fields
            .First(f => f.Name == FIELD_COGS);
        Assert.False(defaulted.IsOverridden);
        Assert.Equal("staff-default-cogs", defaulted.Value);

        SignatureFieldDto overridden = result.Value.Fields
            .First(f => f.Name == FIELD_REVENUE);
        Assert.True(overridden.IsOverridden);
        Assert.Equal("client-override-revenue", overridden.Value);
    }

    [Fact]
    public async Task Handle_Fields_OrderedByFirstTemplateAppearance()
    {
        // The seed's template references COGS before revenue, so the canonical
        // Fields ordering must follow first appearance, not name order.
        await SeedWorkflowWithSignaturesAsync(ORG_ID, WORKFLOW_TYPE_ID, "Test Workflow");

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal([FIELD_COGS, FIELD_REVENUE], result.Value.Fields.Select(f => f.Name).ToList());
    }

    [Fact]
    public async Task Handle_SharedField_AppearsOnceInFields_WithBothBlocksBound()
    {
        // One workflow-level field referenced by an AiQuery block AND a Merge
        // block: it must appear exactly once in Fields, with both block ids in
        // UsedByBlockIds, and in both blocks' FieldNames.
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        _ = new WorkflowSignatureField(root, "shared_field")
        {
            DisplayName = "Shared",
            Description = "desc",
            DefaultValue = "shared-default",
        };
        var section = new Section("Section A", root) { Order = 0 };
        var aiBlock = new Block(BlockTypes.AiQuery, section)
        {
            Name = "AI Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt {shared_field}" },
        };
        var mergeBlock = new Block(BlockTypes.Merge, section)
        {
            Name = "Merge Block",
            Properties = new MergeProperties { TextTemplate = "text {shared_field}" },
        };
        _ = _context.Workflows.Add(root);
        SeedAccess(ORG_ID, WORKFLOW_TYPE_ID);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SignatureFieldDto field = Assert.Single(result.Value.Fields);
        Assert.Equal("shared_field", field.Name);
        Assert.Equal(2, field.UsedByBlockIds.Count);
        Assert.Contains(aiBlock.StableBlockId!, field.UsedByBlockIds);
        Assert.Contains(mergeBlock.StableBlockId!, field.UsedByBlockIds);

        IReadOnlyList<SignatureBlockDto> blocks = result.Value.Sections.Single().Blocks;
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.Equal(["shared_field"], b.FieldNames));
    }

    [Fact]
    public async Task Handle_UnreferencedDefinition_SortsLast_WithNoBlockBindings()
    {
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        _ = new WorkflowSignatureField(root, "unreferenced_field")
        {
            DisplayName = "Unreferenced",
            DefaultValue = "u-default",
        };
        _ = new WorkflowSignatureField(root, "used_field")
        {
            DisplayName = "Used",
            DefaultValue = "used-default",
        };
        var section = new Section("Section A", root) { Order = 0 };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Block A",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt {used_field}" },
        };
        _ = _context.Workflows.Add(root);
        SeedAccess(ORG_ID, WORKFLOW_TYPE_ID);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(
            ["used_field", "unreferenced_field"],
            result.Value.Fields.Select(f => f.Name).ToList());
        SignatureFieldDto unreferenced = result.Value.Fields.First(f => f.Name == "unreferenced_field");
        Assert.Empty(unreferenced.UsedByBlockIds);
    }

    [Fact]
    public async Task Handle_NoWorkflowOfType_ReturnsNotFound()
    {
        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, "WT-DoesNotExist");
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_NoAccessJunctionForOrg_ReturnsNotFound()
    {
        // Workflow exists, owned by OTHER_ORG_ID, with an access grant for OTHER_ORG_ID
        // but NOT for ORG_ID. ORG_ID querying must 404 because the access junction
        // is the gate (not the row's OrganizationId).
        await SeedWorkflowWithSignaturesAsync(
            OTHER_ORG_ID,
            WORKFLOW_TYPE_ID,
            workflowName: "Other Org Workflow");

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_OrgHasAccess_WorkflowOwnedByDifferentOrg_ReturnsCanonicalRow()
    {
        // Direct repro of the original shared-row bug: the canonical Workflow row is owned
        // by OTHER_ORG_ID; ORG_ID has only the WorkflowTypeOrganizationAccess grant.
        // Pre-fix this returned 404; post-fix it must resolve the shared row.
        await SeedWorkflowWithSignaturesAsync(
            OTHER_ORG_ID,
            WORKFLOW_TYPE_ID,
            workflowName: "Shared Workflow");
        SeedAccess(ORG_ID, WORKFLOW_TYPE_ID);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(WORKFLOW_TYPE_ID, result.Value.WorkflowTypeId);
        Assert.Equal("Shared Workflow", result.Value.WorkflowName);
    }

    [Fact]
    public async Task Handle_OverrideIsolation_RequesterDoesNotSeeOtherTenantsOverride()
    {
        // Shared workflow row owned by OTHER_ORG_ID (with a SignatureFieldOverride
        // for OTHER_ORG_ID seeded by SeedWorkflowAndGetBlockAsync). ORG_ID queries
        // through the access junction — it must see the staff default, NOT the
        // OTHER_ORG_ID override that lives in the per-tenant overrides table.
        await SeedWorkflowWithSignaturesAsync(OTHER_ORG_ID, WORKFLOW_TYPE_ID, "Shared Workflow");
        SeedAccess(ORG_ID, WORKFLOW_TYPE_ID);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SignatureFieldDto field = result.Value.Fields.First(f => f.Name == FIELD_REVENUE);
        Assert.False(field.IsOverridden);
        Assert.Equal("staff-default-revenue", field.Value);
    }

    [Fact]
    public async Task Handle_SectionsWithoutSignatureBlocks_AreOmitted()
    {
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        _ = new WorkflowSignatureField(root, "field")
        {
            DisplayName = "Field",
            Description = "desc",
            DefaultValue = "default",
        };

        var populatedSection = new Section("Populated", root) { Order = 0 };
        _ = new Block(BlockTypes.AiQuery, populatedSection)
        {
            Name = "Populated Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt {field}" },
        };

        var emptySection = new Section("Empty", root) { Order = 1 };
        _ = new Block(BlockTypes.AiQuery, emptySection)
        {
            Name = "Empty Block",
            Properties = new AiQueryProperties { UserPromptTemplate = "prompt" },
        };

        _ = _context.Workflows.Add(root);
        SeedAccess(ORG_ID, WORKFLOW_TYPE_ID);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Sections);
        Assert.Equal("Populated", result.Value.Sections.First().SectionTitle);
    }

    [Fact]
    public async Task Handle_WithNoPending_LeavesPendingNull_OnAllFields()
    {
        await SeedWorkflowWithSignaturesAsync(ORG_ID, WORKFLOW_TYPE_ID, "Test Workflow");

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.All(result.Value.Fields, f => Assert.Null(f.Pending));
    }

    [Fact]
    public async Task Handle_WithPending_ProjectsPendingWithApproveAndRejectCounts()
    {
        _ = await SeedWorkflowAndGetBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_COGS,
            ProposedValue = "proposed-cogs",
            ProposerUserId = "user-proposer",
        };
        _ = _context.PendingSignatureOverrides.Add(pending);
        AddVote(pending.Id, "user-proposer", SignatureVoteKind.Approve);
        AddVote(pending.Id, "user-approver-1", SignatureVoteKind.Approve);
        AddVote(pending.Id, "user-rejecter-1", SignatureVoteKind.Reject);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        SignatureFieldDto cogsField = result.Value.Fields.First(f => f.Name == FIELD_COGS);
        Assert.NotNull(cogsField.Pending);
        Assert.Equal(pending.Id, cogsField.Pending!.Id);
        Assert.Equal("user-proposer", cogsField.Pending.ProposerUserId);
        Assert.Equal("proposed-cogs", cogsField.Pending.ProposedValue);
        Assert.Equal(2, cogsField.Pending.ApproveCount);
        Assert.Equal(1, cogsField.Pending.RejectCount);

        // Other field should not pick up the pending.
        SignatureFieldDto revenueField = result.Value.Fields.First(f => f.Name == FIELD_REVENUE);
        Assert.Null(revenueField.Pending);
    }

    [Fact]
    public async Task Handle_WithPending_SetsMyVoteWhenRequesterVoted_ElseNull()
    {
        _ = await SeedWorkflowAndGetBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_COGS,
            ProposedValue = "proposed-cogs",
            ProposerUserId = "user-proposer",
        };
        _ = _context.PendingSignatureOverrides.Add(pending);
        AddVote(pending.Id, "user-proposer", SignatureVoteKind.Approve);
        AddVote(pending.Id, USER_ID, SignatureVoteKind.Reject);
        AddVote(pending.Id, "user-other", SignatureVoteKind.Approve);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Requester voted Reject.
        var queryWhenVoted = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> voted = await _handler.Handle(
            queryWhenVoted, TestContext.Current.CancellationToken);
        PendingSignatureOverrideDto? votedPending = voted.Value.Fields
            .First(f => f.Name == FIELD_COGS).Pending;
        Assert.NotNull(votedPending);
        Assert.Equal(ContractsEnums.SignatureVoteKind.Reject, votedPending!.MyVote);

        // Different requester has no vote → null.
        var queryWhenNotVoted = new GetWorkflowSignaturesQuery(ORG_ID, "user-absent", WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> notVoted = await _handler.Handle(
            queryWhenNotVoted, TestContext.Current.CancellationToken);
        PendingSignatureOverrideDto? notVotedPending = notVoted.Value.Fields
            .First(f => f.Name == FIELD_COGS).Pending;
        Assert.NotNull(notVotedPending);
        Assert.Null(notVotedPending!.MyVote);

        // Proposer sees their seeded Approve as MyVote.
        var queryWhenProposer = new GetWorkflowSignaturesQuery(ORG_ID, "user-proposer", WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> proposerResult = await _handler.Handle(
            queryWhenProposer, TestContext.Current.CancellationToken);
        PendingSignatureOverrideDto? proposerPending = proposerResult.Value.Fields
            .First(f => f.Name == FIELD_COGS).Pending;
        Assert.NotNull(proposerPending);
        Assert.Equal(ContractsEnums.SignatureVoteKind.Approve, proposerPending!.MyVote);
    }

    [Fact]
    public async Task Handle_Pending_ProjectsCommentsOnlyFromVotesWithNonNullComment()
    {
        _ = await SeedWorkflowAndGetBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_COGS,
            ProposedValue = "proposed-cogs",
            ProposerUserId = "user-proposer",
        };
        _ = _context.PendingSignatureOverrides.Add(pending);
        // No comment - should not surface.
        AddVote(pending.Id, "user-proposer", SignatureVoteKind.Approve, comment: null);
        AddVote(pending.Id, "user-silent", SignatureVoteKind.Approve, comment: null);
        // Two with comments, different timestamps — must come back in ascending order.
        AddVote(
            pending.Id,
            "user-commenter-b",
            SignatureVoteKind.Reject,
            comment: "second",
            createdAt: DateTime.UtcNow.AddMinutes(5));
        AddVote(
            pending.Id,
            "user-commenter-a",
            SignatureVoteKind.Reject,
            comment: "first",
            createdAt: DateTime.UtcNow.AddMinutes(1));
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        PendingSignatureOverrideDto? dto = result.Value.Fields
            .First(f => f.Name == FIELD_COGS).Pending;
        Assert.NotNull(dto);
        Assert.Equal(2, dto!.Comments.Count);
        Assert.Equal("first", dto.Comments[0].Comment);
        Assert.Equal("user-commenter-a", dto.Comments[0].VoterUserId);
        Assert.Equal("second", dto.Comments[1].Comment);
        Assert.Equal("user-commenter-b", dto.Comments[1].VoterUserId);
    }

    [Fact]
    public async Task Handle_Pending_ProjectsSuggestionsOnlyFromVotesWithNonNullSuggestedValue()
    {
        _ = await SeedWorkflowAndGetBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);

        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_COGS,
            ProposedValue = "proposed-cogs",
            ProposerUserId = "user-proposer",
        };
        _ = _context.PendingSignatureOverrides.Add(pending);
        AddVote(pending.Id, "user-proposer", SignatureVoteKind.Approve);
        // Rejection with a suggestion.
        AddVote(
            pending.Id,
            "user-suggester",
            SignatureVoteKind.Reject,
            comment: "try this",
            suggestedValue: "alt-value",
            createdAt: DateTime.UtcNow.AddMinutes(2));
        // Rejection without a suggestion.
        AddVote(
            pending.Id,
            "user-no-suggestion",
            SignatureVoteKind.Reject,
            comment: "no",
            suggestedValue: null,
            createdAt: DateTime.UtcNow.AddMinutes(3));
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        PendingSignatureOverrideDto? dto = result.Value.Fields
            .First(f => f.Name == FIELD_COGS).Pending;
        Assert.NotNull(dto);
        SuggestionDto only = Assert.Single(dto!.Suggestions);
        Assert.Equal("user-suggester", only.VoterUserId);
        Assert.Equal("alt-value", only.SuggestedValue);
        Assert.Equal("try this", only.Comment);
    }

    [Fact]
    public async Task Handle_Pending_IgnoresPendingsFromOtherOrgsAndWorkflowTypes()
    {
        // Single shared workflow row. Both ORG_ID and OTHER_ORG_ID have access; their
        // pendings target the SAME field name but are scoped per tenant by
        // OrganizationId on the pending row. A third pending references a different
        // workflow type — all but the requester's must be filtered out.
        _ = await SeedWorkflowAndGetBlockAsync(ORG_ID, WORKFLOW_TYPE_ID);
        SeedAccess(OTHER_ORG_ID, WORKFLOW_TYPE_ID);
        _ = await SeedWorkflowAndGetBlockAsync(ORG_ID, "WT-Other");

        var pendingThisOrg = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_COGS,
            ProposedValue = "mine",
            ProposerUserId = "user-proposer",
        };
        var pendingOtherOrg = new PendingSignatureOverride
        {
            OrganizationId = OTHER_ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_COGS,
            ProposedValue = "other-org",
            ProposerUserId = "user-other-org",
        };
        var pendingOtherType = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = "WT-Other",
            PlaceholderName = FIELD_COGS,
            ProposedValue = "other-type",
            ProposerUserId = "user-other-type",
        };
        _ = _context.PendingSignatureOverrides.Add(pendingThisOrg);
        _ = _context.PendingSignatureOverrides.Add(pendingOtherOrg);
        _ = _context.PendingSignatureOverrides.Add(pendingOtherType);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        // Only the matching pending should surface; all others must be hidden.
        Assert.False(result.IsError);
        IEnumerable<PendingSignatureOverrideDto?> allPending = result.Value.Fields
            .Select(f => f.Pending);
        PendingSignatureOverrideDto onlyVisible = Assert.Single(allPending.OfType<PendingSignatureOverrideDto>());
        Assert.Equal("mine", onlyVisible.ProposedValue);
    }

    [Fact]
    public async Task Handle_ReviewThreshold_ReflectsOrganisationSetting()
    {
        await SeedWorkflowWithSignaturesAsync(ORG_ID, WORKFLOW_TYPE_ID, "Test Workflow");
        await SeedOrganizationAsync(ORG_ID, threshold: 5);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(5, result.Value.ReviewThreshold);
    }

    [Fact]
    public async Task Handle_ReviewThreshold_FallsBackToTwo_WhenOrganisationMissing()
    {
        await SeedWorkflowWithSignaturesAsync(ORG_ID, WORKFLOW_TYPE_ID, "Test Workflow");

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.ReviewThreshold);
    }

    [Fact]
    public async Task Handle_MergeBlockWithSignatureFields_AppearsInResponse()
    {
        // Merge blocks also reference workflow-level signature fields via {tokens}
        // in their TextTemplate. The handler must include Merge blocks when
        // projecting sections and resolve the per-tenant override by field name.
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        _ = new WorkflowSignatureField(root, "tone")
        {
            DisplayName = "Tone",
            Description = "desc",
            DefaultValue = "formal",
        };

        var section = new Section("Section A", root) { Order = 0 };

        _ = new Block(BlockTypes.Merge, section)
        {
            Name = "Boilerplate Block",
            ReplacementTag = "boilerplate",
            Properties = new MergeProperties { TextTemplate = "Use {tone} please." },
        };

        _ = _context.Workflows.Add(root);
        _ = _context.SignatureFieldOverrides.Add(new SignatureFieldOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = "tone",
            Value = "cheerful",
        });
        SeedAccess(ORG_ID, WORKFLOW_TYPE_ID);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SignatureBlockDto blockDto = result.Value.Sections.Single().Blocks.Single();
        Assert.Equal("Boilerplate Block", blockDto.BlockName);
        Assert.Equal(["tone"], blockDto.FieldNames);

        SignatureFieldDto field = Assert.Single(result.Value.Fields);
        Assert.Equal("tone", field.Name);
        Assert.True(field.IsOverridden);
        Assert.Equal("cheerful", field.Value);
        Assert.Equal("formal", field.DefaultValue);

        // BlockType lets the FE branch on block kind (e.g. hide the AI-prompt-replay
        // Test button when the block isn't an AI Query).
        Assert.Equal(nameof(BlockTypes.Merge), blockDto.BlockType);
    }

    [Fact]
    public async Task Handle_AiQueryBlock_BlockTypeReflectsAiQuery()
    {
        await SeedWorkflowWithSignaturesAsync(ORG_ID, WORKFLOW_TYPE_ID, "Test Workflow");

        var query = new GetWorkflowSignaturesQuery(ORG_ID, USER_ID, WORKFLOW_TYPE_ID);
        ErrorOr<WorkflowSignaturesDto> result = await _handler.Handle(
            query, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        SignatureBlockDto blockDto = result.Value.Sections.Single().Blocks.Single();
        Assert.Equal(nameof(BlockTypes.AiQuery), blockDto.BlockType);
    }

    private async Task SeedWorkflowWithSignaturesAsync(
        string orgId,
        string workflowTypeId,
        string workflowName)
    {
        _ = await SeedWorkflowAndGetBlockAsync(orgId, workflowTypeId, workflowName);
    }

    private async Task<Block> SeedWorkflowAndGetBlockAsync(
        string orgId,
        string workflowTypeId,
        string workflowName = "Test Workflow")
    {
        var root = new Workflow(workflowName)
        {
            OrganizationId = orgId,
            WorkflowTypeId = workflowTypeId,
        };
        _ = new WorkflowSignatureField(root, FIELD_COGS)
        {
            DisplayName = "COGS",
            Description = "desc",
            DefaultValue = "staff-default-cogs",
        };
        _ = new WorkflowSignatureField(root, FIELD_REVENUE)
        {
            DisplayName = "Revenue",
            Description = "desc",
            DefaultValue = "staff-default-revenue",
        };

        var section = new Section("Section A", root) { Order = 0 };

        var block = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Revenue Block",
            ReplacementTag = "revenue",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate =
                    "prompt {" + FIELD_COGS + "} then {" + FIELD_REVENUE + "}",
            },
        };

        _ = _context.Workflows.Add(root);
        _ = _context.SignatureFieldOverrides.Add(new SignatureFieldOverride
        {
            OrganizationId = orgId,
            WorkflowTypeId = workflowTypeId,
            PlaceholderName = FIELD_REVENUE,
            Value = "client-override-revenue",
        });
        SeedAccess(orgId, workflowTypeId);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return block;
    }

    private void SeedAccess(string orgId, string workflowTypeId)
    {
        _ = _context.WorkflowTypeOrganizationAccess.Add(new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = workflowTypeId,
            OrganizationId = orgId,
        });
    }

    private async Task SeedOrganizationAsync(string orgId, int threshold)
    {
        bool exists = await _userDataContext.Organizations
            .AnyAsync(o => o.Id == orgId, TestContext.Current.CancellationToken);
        if (exists)
        {
            return;
        }

        var org = new IniaAi.Domain.Entities.UserManagement.Organization("Test Org", customId: orgId)
        {
            SignatureReviewThreshold = threshold,
        };
        _ = _userDataContext.Organizations.Add(org);
        _ = await _userDataContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private void AddVote(
        string pendingOverrideId,
        string voterUserId,
        SignatureVoteKind voteKind,
        string? comment = null,
        string? suggestedValue = null,
        DateTime? createdAt = null)
    {
        var vote = new SignatureOverrideVote
        {
            PendingOverrideId = pendingOverrideId,
            VoterUserId = voterUserId,
            VoteKind = voteKind,
            Comment = comment,
            SuggestedValue = suggestedValue,
        };

        if (createdAt is not null)
        {
            vote.CreatedAt = createdAt.Value;
        }

        _ = _context.SignatureOverrideVotes.Add(vote);
    }
}
