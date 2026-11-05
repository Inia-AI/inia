using ErrorOr;
using IniaAi.Application.Commands.Signatures.CastSignatureOverrideVote;
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

namespace IniaAi.Application.UnitTests.Commands.Signatures.CastSignatureOverrideVote;

public class CastSignatureOverrideVoteCommandHandlerTests : IDisposable
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
    private readonly CastSignatureOverrideVoteCommandHandler _handler;

    public CastSignatureOverrideVoteCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        _userDataContext = _serviceProvider.GetRequiredService<UserDataDbContext>();

        ICoreUnitOfWork coreUnitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        IUserDataUnitOfWork userDataUnitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new CastSignatureOverrideVoteCommandHandler(
            coreUnitOfWork,
            userDataUnitOfWork,
            NullLogger<CastSignatureOverrideVoteCommandHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _userDataContext.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_Approve_IncrementsCount_NoPromotionBelowThreshold()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);
        (PendingSignatureOverride pending, _) = await SeedPendingAndBlockAsync(
            proposedValue: "proposed", seededOverride: null);

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, pending.Id,
            SignatureVoteKind.Approve, Comment: null, SuggestedValue: null);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        PendingSignatureOverride stored = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .Include(p => p.Votes)
            .FirstAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, stored.Votes.Count);
        Assert.Contains(stored.Votes, v => v.VoterUserId == REVIEWER_ID && v.VoteKind == SignatureVoteKind.Approve);
    }

    [Fact]
    public async Task Handle_Reject_IncrementsCount_NoCancellationBelowThreshold()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);
        (PendingSignatureOverride pending, _) = await SeedPendingAndBlockAsync(
            proposedValue: "proposed", seededOverride: null);

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, pending.Id,
            SignatureVoteKind.Reject, Comment: "not great", SuggestedValue: "alt");

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        PendingSignatureOverride stored = await _context.PendingSignatureOverrides
            .AsNoTracking()
            .Include(p => p.Votes)
            .FirstAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, stored.Votes.Count);
        Assert.Contains(stored.Votes, v => v.VoterUserId == REVIEWER_ID && v.VoteKind == SignatureVoteKind.Reject);
    }

    [Fact]
    public async Task Handle_DuplicateVote_ReturnsSignaturesAlreadyVotedConflict()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);
        (PendingSignatureOverride pending, _) = await SeedPendingAndBlockAsync(
            proposedValue: "proposed", seededOverride: null);

        // Seed an existing vote from the same reviewer.
        _ = _context.SignatureOverrideVotes.Add(new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = REVIEWER_ID,
            VoteKind = SignatureVoteKind.Approve,
        });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, pending.Id,
            SignatureVoteKind.Reject, Comment: null, SuggestedValue: null);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal("Signatures.AlreadyVoted", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_ProposerCannotVote_ReturnsValidationError()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);
        (PendingSignatureOverride pending, _) = await SeedPendingAndBlockAsync(
            proposedValue: "proposed", seededOverride: null);

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, PROPOSER_ID, pending.Id,
            SignatureVoteKind.Approve, Comment: null, SuggestedValue: null);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal("Signatures.ProposerCannotVote", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_ApproveAtThreshold_PromotesValue_DeletesPending()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, _) = await SeedPendingAndBlockAsync(
            proposedValue: "new-client-value", seededOverride: null);

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, pending.Id,
            SignatureVoteKind.Approve, Comment: null, SuggestedValue: null);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(
                o => o.OrganizationId == ORG_ID
                    && o.WorkflowTypeId == WORKFLOW_TYPE_ID
                    && o.PlaceholderName == FIELD_A,
                TestContext.Current.CancellationToken);
        Assert.Equal("new-client-value", row.Value);

        Assert.False(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken));
        Assert.False(await _context.SignatureOverrideVotes
            .AsNoTracking()
            .AnyAsync(v => v.PendingOverrideId == pending.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_ApproveAtThreshold_WritesAuditRow_WithProposerAsActor()
    {
        // Audit row's ActorUserId should be the proposer of the pending — NOT the
        // threshold-crossing voter (REVIEWER_ID here). The History UI shows "who put
        // this value forward", which is always the proposer.
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, Block block) = await SeedPendingAndBlockAsync(
            proposedValue: "promoted-value", seededOverride: null);

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, pending.Id,
            SignatureVoteKind.Approve, Comment: null, SuggestedValue: null);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);
        Assert.False(result.IsError);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignatureAuditAction.CREATED, audit.Action);
        Assert.Equal("promoted-value", audit.NewValue);
        Assert.Null(audit.PreviousValue);
        Assert.Equal(PROPOSER_ID, audit.ActorUserId);
        Assert.NotEqual(REVIEWER_ID, audit.ActorUserId);
        Assert.Equal(WORKFLOW_TYPE_ID, audit.WorkflowTypeId);
        // Post-hoist audit rows carry the referencing-block-names snapshot, not a block id.
        Assert.Null(audit.BlockId);
        Assert.NotNull(audit.ReferencedBlockNamesJson);
        Assert.Contains(block.Name, audit.ReferencedBlockNamesJson);
        Assert.Equal(FIELD_A, audit.PlaceholderName);
        Assert.NotNull(audit.ApproversJson);
    }

    [Fact]
    public async Task Handle_ApproveAtThreshold_WithNullProposedValue_RemovesOverride()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, _) = await SeedPendingAndBlockAsync(
            proposedValue: null,
            seededOverride: new Dictionary<string, string> { [FIELD_A] = "existing" });

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, pending.Id,
            SignatureVoteKind.Approve, Comment: null, SuggestedValue: null);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        bool overrideRowExists = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .AnyAsync(
                o => o.OrganizationId == ORG_ID
                    && o.WorkflowTypeId == WORKFLOW_TYPE_ID
                    && o.PlaceholderName == FIELD_A,
                TestContext.Current.CancellationToken);
        Assert.False(overrideRowExists);
    }

    [Fact]
    public async Task Handle_RejectAtThreshold_DeletesPending_KeepsActiveOverride()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, _) = await SeedPendingAndBlockAsync(
            proposedValue: "proposed",
            seededOverride: new Dictionary<string, string> { [FIELD_A] = "existing-active" });

        // One existing reject from another reviewer so that our new reject pushes
        // the count up to threshold=2.
        _ = _context.SignatureOverrideVotes.Add(new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = SECOND_REVIEWER_ID,
            VoteKind = SignatureVoteKind.Reject,
        });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, pending.Id,
            SignatureVoteKind.Reject, Comment: "no thanks", SuggestedValue: null);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        Assert.False(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken));

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(
                o => o.OrganizationId == ORG_ID
                    && o.WorkflowTypeId == WORKFLOW_TYPE_ID
                    && o.PlaceholderName == FIELD_A,
                TestContext.Current.CancellationToken);
        Assert.Equal("existing-active", row.Value);
    }

    [Fact]
    public async Task Handle_PendingNotFound_ReturnsNotFound()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, PendingOverrideId: "does-not-exist",
            SignatureVoteKind.Approve, Comment: null, SuggestedValue: null);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Handle_OrgMismatch_ReturnsNotFound()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, _) = await SeedPendingAndBlockAsync(
            proposedValue: "proposed", seededOverride: null);

        var command = new CastSignatureOverrideVoteCommand(
            OrganizationId: "different-org",
            UserId: REVIEWER_ID,
            PendingOverrideId: pending.Id,
            VoteKind: SignatureVoteKind.Approve,
            Comment: null,
            SuggestedValue: null);

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
    public async Task Handle_MergeBlockApproveAtThreshold_PromotesValue_DeletesPending()
    {
        // Pending overrides on Merge blocks must promote to active overrides on
        // threshold approval, just like AI Query — the block-type guard inside the
        // handler's Promote path is keyed on IHasSignatureFields rather than
        // BlockTypes.AiQuery so any signature-bearing block participates.
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, _) = await SeedMergePendingAndBlockAsync(
            proposedValue: "client-merge-value");

        var command = new CastSignatureOverrideVoteCommand(
            ORG_ID, REVIEWER_ID, pending.Id,
            SignatureVoteKind.Approve, Comment: null, SuggestedValue: null);

        ErrorOr<Success> result = await _handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(
                o => o.OrganizationId == ORG_ID
                    && o.WorkflowTypeId == WORKFLOW_TYPE_ID
                    && o.PlaceholderName == FIELD_A,
                TestContext.Current.CancellationToken);
        Assert.Equal("client-merge-value", row.Value);

        Assert.False(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken));
    }

    private async Task<(PendingSignatureOverride Pending, Block Block)> SeedMergePendingAndBlockAsync(
        string? proposedValue)
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
            ProposedValue = proposedValue,
            ProposerUserId = PROPOSER_ID,
        };
        _ = _context.PendingSignatureOverrides.Add(pending);
        _ = _context.SignatureOverrideVotes.Add(new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = PROPOSER_ID,
            VoteKind = SignatureVoteKind.Approve,
        });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (pending, block);
    }

    private async Task<(PendingSignatureOverride Pending, Block Block)> SeedPendingAndBlockAsync(
        string? proposedValue,
        Dictionary<string, string>? seededOverride)
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

        if (seededOverride is not null)
        {
            foreach (KeyValuePair<string, string> entry in seededOverride)
            {
                _ = _context.SignatureFieldOverrides.Add(new SignatureFieldOverride
                {
                    OrganizationId = ORG_ID,
                    WorkflowTypeId = WORKFLOW_TYPE_ID,
                    PlaceholderName = entry.Key,
                    Value = entry.Value,
                });
            }
        }

        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = proposedValue,
            ProposerUserId = PROPOSER_ID,
        };
        _ = _context.PendingSignatureOverrides.Add(pending);
        _ = _context.SignatureOverrideVotes.Add(new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = PROPOSER_ID,
            VoteKind = SignatureVoteKind.Approve,
        });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (pending, block);
    }
}
