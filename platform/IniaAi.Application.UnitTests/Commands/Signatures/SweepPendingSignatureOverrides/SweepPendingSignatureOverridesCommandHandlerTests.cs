using ErrorOr;
using IniaAi.Application.Commands.Signatures;
using IniaAi.Application.Commands.Signatures.SweepPendingSignatureOverrides;
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
using System.Text.Json;

namespace IniaAi.Application.UnitTests.Commands.Signatures.SweepPendingSignatureOverrides;

public class SweepPendingSignatureOverridesCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string ADMIN_USER_ID = "user-admin";
    private const string PROPOSER_ID = "user-proposer";
    private const string REVIEWER_A_ID = "user-reviewer-a";
    private const string REVIEWER_B_ID = "user-reviewer-b";
    private const string WORKFLOW_TYPE_ID = "WT-TestSignatures";
    private const string FIELD_A = "field_a";
    private const string STAFF_DEFAULT_A = "staff-default-a";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly CoreDbContext _context;
    private readonly UserDataDbContext _userDataContext;
    private readonly SweepPendingSignatureOverridesCommandHandler _handler;

    public SweepPendingSignatureOverridesCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<CoreDbContext>();
        _userDataContext = _serviceProvider.GetRequiredService<UserDataDbContext>();

        ICoreUnitOfWork coreUnitOfWork = _serviceProvider.GetRequiredService<ICoreUnitOfWork>();
        IUserDataUnitOfWork userDataUnitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _handler = new SweepPendingSignatureOverridesCommandHandler(
            coreUnitOfWork,
            userDataUnitOfWork,
            NullLogger<SweepPendingSignatureOverridesCommandHandler>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _userDataContext.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_ThresholdMet_PromotesPending_DeletesPendingAndVotes()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        (PendingSignatureOverride pending, _) = await SeedPendingAsync(
            proposedValue: "swept-value",
            approverIds: [PROPOSER_ID, REVIEWER_A_ID]);

        var command = new SweepPendingSignatureOverridesCommand(ORG_ID, ADMIN_USER_ID);

        ErrorOr<SweepPendingSignatureOverridesResult> result = await _handler.Handle(
            command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.Promoted);
        Assert.Equal(0, result.Value.Cancelled);
        Assert.Equal(0, result.Value.StillPending);

        SignatureFieldOverride row = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("swept-value", row.Value);

        Assert.False(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_ThresholdMet_WritesAuditRow_WithProposerAsActor_AndApproversSnapshot()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        await SeedUserAsync(PROPOSER_ID, "proposer@example.com");
        await SeedUserAsync(REVIEWER_A_ID, "reviewer-a@example.com");
        (PendingSignatureOverride _, Block block) = await SeedPendingAsync(
            proposedValue: "swept-value",
            approverIds: [PROPOSER_ID, REVIEWER_A_ID]);

        var command = new SweepPendingSignatureOverridesCommand(ORG_ID, ADMIN_USER_ID);

        ErrorOr<SweepPendingSignatureOverridesResult> result = await _handler.Handle(
            command, TestContext.Current.CancellationToken);
        Assert.False(result.IsError);

        SignatureFieldOverrideAuditLog audit = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignatureAuditAction.CREATED, audit.Action);
        Assert.Equal("swept-value", audit.NewValue);
        // Actor is the proposer, NOT the admin who triggered the sweep.
        Assert.Equal(PROPOSER_ID, audit.ActorUserId);
        Assert.Equal("proposer@example.com", audit.ActorUserEmail);
        Assert.NotEqual(ADMIN_USER_ID, audit.ActorUserId);
        Assert.Equal(WORKFLOW_TYPE_ID, audit.WorkflowTypeId);
        // Post-hoist audit rows carry the referencing-block-names snapshot, not a block id.
        Assert.Null(audit.BlockId);
        Assert.NotNull(audit.ReferencedBlockNamesJson);
        Assert.Contains(block.Name, audit.ReferencedBlockNamesJson);
        Assert.Equal(FIELD_A, audit.PlaceholderName);
        // Approvers snapshot contains the proposer and the reviewer with resolved
        // emails. Round-trip through the pinned JsonSerializerOptions instead of
        // raw-string search so the assertion survives whitespace/key-order
        // formatting changes — what matters is the contract the History UI's
        // deserializer relies on, not the literal column bytes.
        Assert.NotNull(audit.ApproversJson);
        List<SignatureAuditApprover>? approvers = JsonSerializer.Deserialize<List<SignatureAuditApprover>>(
            audit.ApproversJson!,
            SignatureOverrideWriter.ApproverJsonOptions);
        Assert.NotNull(approvers);
        Assert.Equal(2, approvers!.Count);
        Assert.Contains(approvers, a => a.UserId == PROPOSER_ID && a.Email == "proposer@example.com");
        Assert.Contains(approvers, a => a.UserId == REVIEWER_A_ID && a.Email == "reviewer-a@example.com");
    }

    [Fact]
    public async Task Handle_ThresholdMet_ButFieldNoLongerReferenced_SkipsAndCountsAsStale()
    {
        // The field-validity check inside the promote loop guards against
        // promoting an override for a field that was deleted or renamed out from
        // under the pending. The pending satisfies the threshold but should NOT
        // promote — no override row, no audit row, and `SkippedStale` should
        // account for it (NOT `StillPending`, which is reserved for pendings
        // genuinely awaiting more votes).
        await SeedOrganizationAsync(ORG_ID, threshold: 2);

        // The workflow defines no signature fields and its block references no
        // token, so the pending's field name misses the referenced-fields map.
        var root = new Workflow("Test Workflow")
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
        };
        var section = new Section("Section A", root) { Order = 0 };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Block Without Signature Fields",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "prompt",
            },
        };
        _ = _context.Workflows.Add(root);

        // Pending references a field the saved workflow no longer defines —
        // mirrors the orphan path the editor's save-time cleanup didn't catch.
        var orphanedPending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = "swept-but-stale",
            ProposerUserId = PROPOSER_ID,
        };
        _ = _context.PendingSignatureOverrides.Add(orphanedPending);
        AddVote(orphanedPending.Id, PROPOSER_ID, SignatureVoteKind.Approve);
        AddVote(orphanedPending.Id, REVIEWER_A_ID, SignatureVoteKind.Approve);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SweepPendingSignatureOverridesCommand(ORG_ID, ADMIN_USER_ID);
        ErrorOr<SweepPendingSignatureOverridesResult> result = await _handler.Handle(
            command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.Promoted);
        Assert.Equal(0, result.Value.Cancelled);
        Assert.Equal(0, result.Value.StillPending);
        Assert.Equal(1, result.Value.SkippedStale);

        Assert.False(await _context.SignatureFieldOverrides
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
        // Orphaned pending is left in place — the editor's save-time sync owns
        // removing it when the definition deletion goes through.
        Assert.True(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == orphanedPending.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_RejectThresholdMet_CancelsPending_NoOverride_NoAudit()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        // Proposer auto-approve plus two rejects → reject count (2) meets threshold,
        // approve count (1) does not.
        (PendingSignatureOverride pending, _) = await SeedPendingAsync(
            proposedValue: "rejected-value",
            approverIds: [PROPOSER_ID],
            rejectorIds: [REVIEWER_A_ID, REVIEWER_B_ID]);

        var command = new SweepPendingSignatureOverridesCommand(ORG_ID, ADMIN_USER_ID);

        ErrorOr<SweepPendingSignatureOverridesResult> result = await _handler.Handle(
            command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.Promoted);
        Assert.Equal(1, result.Value.Cancelled);

        Assert.False(await _context.SignatureFieldOverrides
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_BelowThreshold_LeavesPendingAndEmitsNoAudit()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 3);
        (PendingSignatureOverride pending, _) = await SeedPendingAsync(
            proposedValue: "below-threshold",
            approverIds: [PROPOSER_ID, REVIEWER_A_ID]);

        var command = new SweepPendingSignatureOverridesCommand(ORG_ID, ADMIN_USER_ID);

        ErrorOr<SweepPendingSignatureOverridesResult> result = await _handler.Handle(
            command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.Promoted);
        Assert.Equal(0, result.Value.Cancelled);
        Assert.Equal(1, result.Value.StillPending);

        Assert.True(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pending.Id, TestContext.Current.CancellationToken));
        Assert.False(await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_BatchOfPendings_EmitsOneAuditPerPromotion()
    {
        // Two pendings on the same block but different placeholders; one promotable,
        // one not. Sweep should emit exactly one audit row.
        await SeedOrganizationAsync(ORG_ID, threshold: 2);

        Block block = await SeedSignatureBlockAsync();

        var promotablePending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = "promote-me",
            ProposerUserId = PROPOSER_ID,
        };
        _ = _context.PendingSignatureOverrides.Add(promotablePending);
        AddVote(promotablePending.Id, PROPOSER_ID, SignatureVoteKind.Approve);
        AddVote(promotablePending.Id, REVIEWER_A_ID, SignatureVoteKind.Approve);

        var quietPending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = "other_field",
            ProposedValue = "leave-me",
            ProposerUserId = PROPOSER_ID,
        };
        _ = _context.PendingSignatureOverrides.Add(quietPending);
        AddVote(quietPending.Id, PROPOSER_ID, SignatureVoteKind.Approve);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SweepPendingSignatureOverridesCommand(ORG_ID, ADMIN_USER_ID);
        ErrorOr<SweepPendingSignatureOverridesResult> result = await _handler.Handle(
            command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.Promoted);
        Assert.Equal(1, result.Value.StillPending);

        int auditCount = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, auditCount);
    }

    [Fact]
    public async Task Handle_MultiplePromotablePendings_EmitsOneAuditAndOverridePerPending()
    {
        // Two distinct promotable pendings on different fields of the same block.
        // Verifies that the Phase-3 loop writes an override AND audit row for
        // each, preserves per-pending proposer/approver attribution, and that
        // the batched workflow load doesn't accidentally merge the pendings.
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        await SeedUserAsync(PROPOSER_ID, "proposer@example.com");
        const string SECOND_PROPOSER_ID = "user-proposer-2";
        await SeedUserAsync(SECOND_PROPOSER_ID, "proposer2@example.com");
        await SeedUserAsync(REVIEWER_A_ID, "reviewer-a@example.com");

        Block block = await SeedSignatureBlockAsync();

        var pending1 = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = "value-1",
            ProposerUserId = PROPOSER_ID,
        };
        _ = _context.PendingSignatureOverrides.Add(pending1);
        AddVote(pending1.Id, PROPOSER_ID, SignatureVoteKind.Approve);
        AddVote(pending1.Id, REVIEWER_A_ID, SignatureVoteKind.Approve);

        var pending2 = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = "other_field",
            ProposedValue = "value-2",
            ProposerUserId = SECOND_PROPOSER_ID,
        };
        _ = _context.PendingSignatureOverrides.Add(pending2);
        AddVote(pending2.Id, SECOND_PROPOSER_ID, SignatureVoteKind.Approve);
        AddVote(pending2.Id, REVIEWER_A_ID, SignatureVoteKind.Approve);

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SweepPendingSignatureOverridesCommand(ORG_ID, ADMIN_USER_ID);
        ErrorOr<SweepPendingSignatureOverridesResult> result = await _handler.Handle(
            command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.Promoted);

        // Two override rows, one per placeholder.
        int overrideCount = await _context.SignatureFieldOverrides
            .AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, overrideCount);

        // Two audit rows, each carrying its own proposer.
        List<SignatureFieldOverrideAuditLog> audits = await _context.SignatureFieldOverrideAuditLogs
            .AsNoTracking()
            .OrderBy(a => a.PlaceholderName)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Count);

        SignatureFieldOverrideAuditLog auditA = audits.Single(a => a.PlaceholderName == FIELD_A);
        Assert.Equal(PROPOSER_ID, auditA.ActorUserId);
        Assert.Equal("proposer@example.com", auditA.ActorUserEmail);
        Assert.Equal("value-1", auditA.NewValue);

        SignatureFieldOverrideAuditLog auditOther = audits.Single(a => a.PlaceholderName == "other_field");
        Assert.Equal(SECOND_PROPOSER_ID, auditOther.ActorUserId);
        Assert.Equal("proposer2@example.com", auditOther.ActorUserEmail);
        Assert.Equal("value-2", auditOther.NewValue);
    }

    [Fact]
    public async Task Handle_OrgScoped_DoesNotSweepOtherTenants()
    {
        await SeedOrganizationAsync(ORG_ID, threshold: 2);
        await SeedOrganizationAsync("other-org", threshold: 2);

        _ = await SeedSignatureBlockAsync();
        var pendingOtherOrg = new PendingSignatureOverride
        {
            OrganizationId = "other-org",
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = "other-org-value",
            ProposerUserId = PROPOSER_ID,
        };
        _ = _context.PendingSignatureOverrides.Add(pendingOtherOrg);
        AddVote(pendingOtherOrg.Id, PROPOSER_ID, SignatureVoteKind.Approve);
        AddVote(pendingOtherOrg.Id, REVIEWER_A_ID, SignatureVoteKind.Approve);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new SweepPendingSignatureOverridesCommand(ORG_ID, ADMIN_USER_ID);
        ErrorOr<SweepPendingSignatureOverridesResult> result = await _handler.Handle(
            command, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.Promoted);
        Assert.True(await _context.PendingSignatureOverrides
            .AsNoTracking()
            .AnyAsync(p => p.Id == pendingOtherOrg.Id, TestContext.Current.CancellationToken));
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

    private async Task SeedUserAsync(string userId, string email)
    {
        bool exists = await _userDataContext.Users
            .AnyAsync(u => u.Id == userId, TestContext.Current.CancellationToken);
        if (exists)
        {
            return;
        }

        var user = new User
        {
            Id = userId,
            UserName = email,
            Email = email,
            OrganizationId = ORG_ID,
        };
        _ = _userDataContext.Users.Add(user);
        _ = await _userDataContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Block> SeedSignatureBlockAsync()
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
        _ = new WorkflowSignatureField(root, "other_field")
        {
            DisplayName = "Other Field",
            Description = "desc",
            DefaultValue = "other-default",
        };
        var section = new Section("Section A", root) { Order = 0 };
        var block = new Block(BlockTypes.AiQuery, section)
        {
            Name = "Block A",
            Properties = new AiQueryProperties
            {
                UserPromptTemplate = "prompt with {" + FIELD_A + "} and {other_field}",
            },
        };
        _ = _context.Workflows.Add(root);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return block;
    }

    private async Task<(PendingSignatureOverride Pending, Block Block)> SeedPendingAsync(
        string? proposedValue,
        IReadOnlyList<string> approverIds,
        IReadOnlyList<string>? rejectorIds = null)
    {
        Block block = await SeedSignatureBlockAsync();

        var pending = new PendingSignatureOverride
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            PlaceholderName = FIELD_A,
            ProposedValue = proposedValue,
            ProposerUserId = PROPOSER_ID,
        };
        _ = _context.PendingSignatureOverrides.Add(pending);

        foreach (string id in approverIds)
        {
            AddVote(pending.Id, id, SignatureVoteKind.Approve);
        }

        if (rejectorIds is not null)
        {
            foreach (string id in rejectorIds)
            {
                AddVote(pending.Id, id, SignatureVoteKind.Reject);
            }
        }

        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (pending, block);
    }

    private void AddVote(string pendingId, string voterId, SignatureVoteKind kind)
    {
        _ = _context.SignatureOverrideVotes.Add(new SignatureOverrideVote
        {
            PendingOverrideId = pendingId,
            VoterUserId = voterId,
            VoteKind = kind,
        });
    }
}
