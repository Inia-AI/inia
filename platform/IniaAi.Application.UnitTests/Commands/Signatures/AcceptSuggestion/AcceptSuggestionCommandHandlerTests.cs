using ErrorOr;
using IniaAi.Application.Commands.Signatures.AcceptSuggestion;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.Signatures.AcceptSuggestion;

public class AcceptSuggestionCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string PROPOSER_ID = "user-proposer";
    private const string REVIEWER_ID = "user-reviewer";
    private const string SECOND_REVIEWER_ID = "user-second-reviewer";
    private const string WORKFLOW_TYPE_ID = "WT-TestSignatures";
    private const string FIELD_A = "field_a";
    private const string STAFF_DEFAULT_A = "staff-default-a";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly UserDataDbContext _userDataContext;
    private readonly AcceptSuggestionCommandHandler _handler;

    public AcceptSuggestionCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        _userDataContext = _serviceProvider.GetRequiredService<UserDataDbContext>();

        ICoreUnitOfWork coreUnitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        IUserDataUnitOfWork userDataUnitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new AcceptSuggestionCommandHandler(
            coreUnitOfWork,
            userDataUnitOfWork,
            NullLogger<AcceptSuggestionCommandHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _userDataContext.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_ThresholdAbovePair_ReplacesValue_ClearsVotes_SeedsProposerAndSuggesterApprove()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);
        (PendingSignatureOverride pending, SignatureOverrideVote suggestionVote, _) =
            await SeedPendingWithSuggestionAsync();

        var command = new AcceptSuggestionCommand(
            ORG_ID, PROPOSER_ID, pending.Id, suggestionVote.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        PendingSignatureOverride stored = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .Include(p => p.Votes)
            .FirstAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken);

        Assert.Equal("suggested-alternate", stored.ProposedValue);
        Assert.Equal(2, stored.Votes.Count);
        Assert.Contains(stored.Votes, v =>
            v.VoterUserId == PROPOSER_ID && v.VoteKind == SignatureVoteKind.Approve);
        Assert.Contains(stored.Votes, v =>
            v.VoterUserId == REVIEWER_ID && v.VoteKind == SignatureVoteKind.Approve);
        Assert.DoesNotContain(stored.Votes, v => v.VoterUserId == SECOND_REVIEWER_ID);
        Assert.All(stored.Votes, v =>
        {
            Assert.Null(v.Comment);
            Assert.Null(v.SuggestedValue);
        });
    }

    [Fact]
    public async Task Handle_ThresholdEqualsPair_PromotesOverrideAndDeletesPending()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, SignatureOverrideVote suggestionVote, _) =
            await SeedPendingWithSuggestionAsync();

        var command = new AcceptSuggestionCommand(
            ORG_ID, PROPOSER_ID, pending.Id, suggestionVote.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        bool pendingExists = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken);
        Assert.False(pendingExists);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(
                o => o.OrganizationId == ORG_ID
                    && o.WorkflowTypeId == WORKFLOW_TYPE_ID
                    && o.PlaceholderName == FIELD_A,
                TestContext.Current.CancellationToken);
        Assert.Equal("suggested-alternate", row.Value);
    }

    [Fact]
    public async Task Handle_ThresholdEqualsPair_WritesAuditRow_WithProposerAsActor()
    {
        // Same flow as the previous test, but asserting the audit row contract:
        // actor is the proposer (PROPOSER_ID), not the suggester, and approvers
        // include both the proposer's auto-approve and the suggester's seeded
        // approve.
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, SignatureOverrideVote suggestionVote, Block block) =
            await SeedPendingWithSuggestionAsync();

        var command = new AcceptSuggestionCommand(
            ORG_ID, PROPOSER_ID, pending.Id, suggestionVote.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);
        Assert.False(result.IsError);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignatureAuditAction.CREATED, audit.Action);
        Assert.Equal("suggested-alternate", audit.NewValue);
        Assert.Equal(PROPOSER_ID, audit.ActorUserId);
        Assert.Equal(WORKFLOW_TYPE_ID, audit.WorkflowTypeId);
        // Post-hoist audit rows carry the referencing-block-names snapshot, not a block id.
        Assert.Null(audit.BlockId);
        Assert.NotNull(audit.ReferencedBlockNamesJson);
        Assert.Contains(block.Name, audit.ReferencedBlockNamesJson);
        Assert.Equal(FIELD_A, audit.PlaceholderName);
        Assert.NotNull(audit.ApproversJson);
    }

    [Fact]
    public async Task Handle_NonProposer_ReturnsForbidden()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);
        (PendingSignatureOverride pending, SignatureOverrideVote suggestionVote, _) =
            await SeedPendingWithSuggestionAsync();

        var command = new AcceptSuggestionCommand(
            ORG_ID, REVIEWER_ID, pending.Id, suggestionVote.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
        Assert.Equal("Signatures.NotProposer", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_VoteHasNoSuggestedValue_ReturnsValidationError()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);
        PendingSignatureOverride pending = await SeedPendingWithCommentOnlyRejectAsync();

        SignatureOverrideVote rejectVote = pending.Votes.First(v => v.VoteKind == SignatureVoteKind.Reject);
        var command = new AcceptSuggestionCommand(
            ORG_ID, PROPOSER_ID, pending.Id, rejectVote.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Signatures.NoSuggestedValue", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_VoteFromDifferentPending_ReturnsNotFound()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);
        (PendingSignatureOverride pending, _, _) = await SeedPendingWithSuggestionAsync();

        var secondPending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = "other_field",
            ProposedValue = "other",
            ProposerUserId = PROPOSER_ID,
        };
        var otherVote = new SignatureOverrideVote
        {
            PendingOverrideId = secondPending.Id,
            VoterUserId = REVIEWER_ID,
            VoteKind = SignatureVoteKind.Reject,
            SuggestedValue = "alt",
        };
        _ = _context.PendingSignatureOverrides.Add(secondPending);
        _ = _context.SignatureOverrideVotes.Add(otherVote);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AcceptSuggestionCommand(
            ORG_ID, PROPOSER_ID, pending.Id, otherVote.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("SignatureOverrideVote.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_PendingNotFoundForOrg_ReturnsNotFound()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);

        var command = new AcceptSuggestionCommand(
            ORG_ID, PROPOSER_ID, PendingOverrideId: "does-not-exist", VoteId: "vote-x");

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    private async Task SeedOrganizationAsync(string orgId, int threshold)
    {
        bool exists = await _userDataContext.Organizations
            .AnyAsync(o => o.Id == orgId, TestContext.Current.CancellationToken);
        if (exists)
        {
            return;
        }

        var org = new Organization("Test Org", customId: orgId)
        {
            SignatureReviewThreshold = threshold,
        };
        _ = _userDataContext.Organizations.Add(org);
        _ = await _userDataContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_MergeBlockThresholdEqualsPair_PromotesOverrideAndDeletesPending()
    {
        // Accepting a suggestion on a Merge block must promote to the active
        // overrides table at threshold, exactly like AI Query — the block-type
        // guard inside the promotion path is keyed on IHasSignatureFields rather
        // than BlockTypes.AiQuery.
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, SignatureOverrideVote suggestionVote, _) =
            await SeedMergePendingWithSuggestionAsync();

        var command = new AcceptSuggestionCommand(
            ORG_ID, PROPOSER_ID, pending.Id, suggestionVote.Id);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        bool pendingExists = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken);
        Assert.False(pendingExists);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(
                o => o.OrganizationId == ORG_ID
                    && o.WorkflowTypeId == WORKFLOW_TYPE_ID
                    && o.PlaceholderName == FIELD_A,
                TestContext.Current.CancellationToken);
        Assert.Equal("suggested-alternate", row.Value);
    }

    private async Task<(PendingSignatureOverride Pending, SignatureOverrideVote Suggestion, Block Block)>
        SeedMergePendingWithSuggestionAsync()
    {
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        _ = new WorkflowSignatureField(root, FIELD_A)
        {
            DisplayName = "Field A",
            Description = "desc",
            DefaultValue = STAFF_DEFAULT_A,
        };
        var section = new Section("Section A", root) { Order = 0 };
        var block = new Block(BlockTypes.Merge, section)
        {
            Name = "Merge Block A",
            Properties = new MergeProperties
            {
                TextTemplate = "Use {" + FIELD_A + "} please.",
            },
        };

        _ = _context.Workflows.Add(root);

        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = "original-proposed",
            ProposerUserId = PROPOSER_ID,
        };

        var proposerApprove = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = PROPOSER_ID,
            VoteKind = SignatureVoteKind.Approve,
        };
        var suggestionReject = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = REVIEWER_ID,
            VoteKind = SignatureVoteKind.Reject,
            Comment = "try this instead",
            SuggestedValue = "suggested-alternate",
        };

        _ = _context.PendingSignatureOverrides.Add(pending);
        _context.SignatureOverrideVotes.AddRange(proposerApprove, suggestionReject);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (pending, suggestionReject, block);
    }

    private async Task<(PendingSignatureOverride Pending, SignatureOverrideVote Suggestion, Block Block)>
        SeedPendingWithSuggestionAsync()
    {
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        _ = new WorkflowSignatureField(root, FIELD_A)
        {
            DisplayName = "Field A",
            Description = "desc",
            DefaultValue = STAFF_DEFAULT_A,
        };
        var section = new Section("Section A", root) { Order = 0 };
        var block = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Block A",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "prompt with {" + FIELD_A + "}",
            },
        };

        _ = _context.Workflows.Add(root);

        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = "original-proposed",
            ProposerUserId = PROPOSER_ID,
        };

        var proposerApprove = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = PROPOSER_ID,
            VoteKind = SignatureVoteKind.Approve,
        };
        var suggestionReject = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = REVIEWER_ID,
            VoteKind = SignatureVoteKind.Reject,
            Comment = "try this instead",
            SuggestedValue = "suggested-alternate",
        };
        var extraReject = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = SECOND_REVIEWER_ID,
            VoteKind = SignatureVoteKind.Reject,
            Comment = "no from me too",
            SuggestedValue = null,
        };

        _ = _context.PendingSignatureOverrides.Add(pending);
        _context.SignatureOverrideVotes.AddRange(proposerApprove, suggestionReject, extraReject);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (pending, suggestionReject, block);
    }

    private async Task<PendingSignatureOverride> SeedPendingWithCommentOnlyRejectAsync()
    {
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        _ = new WorkflowSignatureField(root, FIELD_A)
        {
            DisplayName = "Field A",
            Description = "desc",
            DefaultValue = STAFF_DEFAULT_A,
        };
        var section = new Section("Section A", root) { Order = 0 };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Block A",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "prompt with {" + FIELD_A + "}",
            },
        };
        _ = _context.Workflows.Add(root);

        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = "original-proposed",
            ProposerUserId = PROPOSER_ID,
        };

        var proposerApprove = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = PROPOSER_ID,
            VoteKind = SignatureVoteKind.Approve,
        };
        var rejectNoSuggestion = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = REVIEWER_ID,
            VoteKind = SignatureVoteKind.Reject,
            Comment = "Just no",
            SuggestedValue = null,
        };

        pending.Votes.Add(proposerApprove);
        pending.Votes.Add(rejectNoSuggestion);

        _ = _context.PendingSignatureOverrides.Add(pending);
        _context.SignatureOverrideVotes.AddRange(proposerApprove, rejectNoSuggestion);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return pending;
    }
}
