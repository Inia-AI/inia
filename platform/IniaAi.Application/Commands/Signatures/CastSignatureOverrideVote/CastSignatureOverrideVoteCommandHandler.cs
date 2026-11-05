using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Specifications.Signatures;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Signatures.CastSignatureOverrideVote;

/// <summary>
/// Inserts a reviewer vote on a pending override; if the configured tenant
/// threshold is reached this call also promotes (write override + delete
/// pending) or cancels (delete pending) the pending row. All DB writes happen
/// under a single <see cref="ICoreUnitOfWork.CompleteAsync"/> call so EF
/// persists them atomically — there is no separate transaction scope because
/// the interface does not expose one and the existing test doubles rely on
/// CompleteAsync semantics.
/// </summary>
public class CastSignatureOverrideVoteCommandHandler
    : IRequestHandler<CastSignatureOverrideVoteCommand, ErrorOr<Success>>
{
    private const string ERROR_PENDING_NOT_FOUND = "PendingSignatureOverride.NotFound";
    private const string ERROR_SIGNATURES_STALE = "Signatures.Stale";
    private const string ERROR_PROPOSER_CANNOT_VOTE = "Signatures.ProposerCannotVote";
    private const string ERROR_ALREADY_VOTED = "Signatures.AlreadyVoted";
    private const int MIN_THRESHOLD = 2;

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ILogger<CastSignatureOverrideVoteCommandHandler> _logger;

    public CastSignatureOverrideVoteCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IUserDataUnitOfWork userDataUnitOfWork,
        ILogger<CastSignatureOverrideVoteCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<Success>> Handle(
        CastSignatureOverrideVoteCommand request,
        CancellationToken cancellationToken)
    {
        IRepository<PendingSignatureOverride> pendingRepo =
            _coreUnitOfWork.Repository<PendingSignatureOverride>();

        PendingSignatureOverride? pending = await pendingRepo
            .GetAll()
            .Where(p => p.Id == request.PendingOverrideId
                && p.OrganizationId == request.OrganizationId)
            .Include(p => p.Votes)
            .FirstOrDefaultAsync(cancellationToken);

        if (pending is null)
        {
            return Error.NotFound(
                ERROR_PENDING_NOT_FOUND,
                $"Pending override '{request.PendingOverrideId}' was not found for this organisation.");
        }

        if (pending.ProposerUserId == request.UserId)
        {
            return Error.Validation(
                ERROR_PROPOSER_CANNOT_VOTE,
                "The proposer cannot cast an additional vote on their own pending override.");
        }

        // Pre-check duplicate in-memory from the already-loaded votes. The
        // unique index on (PendingOverrideId, VoterUserId) is the source of
        // truth on real providers — we still catch DbUpdateException below to
        // cover concurrent races.
        bool alreadyVoted = pending.Votes.Any(v => v.VoterUserId == request.UserId);
        if (alreadyVoted)
        {
            return Error.Conflict(
                ERROR_ALREADY_VOTED,
                "This reviewer has already voted on this pending override.");
        }

        var newVote = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = request.UserId,
            VoteKind = request.VoteKind,
            Comment = request.Comment,
            SuggestedValue = request.SuggestedValue,
        };

        // EF's relationship fixup adds newVote to pending.Votes when we insert
        // a tracked vote whose PendingOverrideId matches a loaded pending — the
        // explicit collection add below would double-count. We only add to the
        // repo; the navigation collection is updated by change tracking.
        await _coreUnitOfWork.Repository<SignatureOverrideVote>().AddAsync(newVote);

        int approveCount = 0;
        int rejectCount = 0;
        foreach (SignatureOverrideVote v in pending.Votes)
        {
            if (v.VoteKind is SignatureVoteKind.Approve)
            {
                approveCount++;
                continue;
            }

            rejectCount++;
        }

        int threshold = await LoadTenantThresholdAsync(request.OrganizationId, cancellationToken);

        bool shouldPromote = approveCount >= threshold;
        bool shouldCancel = !shouldPromote && rejectCount >= threshold;

        if (shouldPromote)
        {
            ErrorOr<Success> promoteResult = await PromoteAsync(pending, cancellationToken);
            if (promoteResult.IsError)
            {
                return promoteResult;
            }

            pendingRepo.HardRemove(pending);
        }
        else if (shouldCancel)
        {
            pendingRepo.HardRemove(pending);
        }

        try
        {
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueVoteViolation(ex))
        {
            _logger.LogInformation(
                ex,
                "Duplicate vote detected on pending {PendingId} by user {UserId}",
                pending.Id,
                request.UserId);
            return Error.Conflict(
                ERROR_ALREADY_VOTED,
                "This reviewer has already voted on this pending override.");
        }

        _logger.LogInformation(
            "Cast {VoteKind} vote on pending {PendingId}; approves={ApproveCount} rejects={RejectCount} threshold={Threshold}",
            request.VoteKind,
            pending.Id,
            approveCount,
            rejectCount,
            threshold);

        return Result.Success;
    }

    private async Task<int> LoadTenantThresholdAsync(string organizationId, CancellationToken cancellationToken)
    {
        Organization? organization = await _userDataUnitOfWork.Repository<Organization>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

        if (organization is null || organization.SignatureReviewThreshold < MIN_THRESHOLD)
        {
            // Fall back to the enforced minimum. The Organization.cs [Range(2,…)]
            // constraint means this branch should not be hit in production, but
            // hardening against a mis-seeded row avoids an early-promotion
            // surprise if the column ever drifted.
            return MIN_THRESHOLD;
        }

        return organization.SignatureReviewThreshold;
    }

    private async Task<ErrorOr<Success>> PromoteAsync(
        PendingSignatureOverride pending,
        CancellationToken cancellationToken)
    {
        // Workflow rows are shared across tenants; access is implied by the pending
        // row's OrganizationId match enforced upstream. The spec applies the same
        // OrderByDescending(CreatedAt) tiebreaker as the workflow execution path so
        // we resolve to the same canonical row that runs the workflow.
        var workflowSpec = new WorkflowWithSectionsAndBlocksByTypeSpec(pending.WorkflowTypeId);
        Workflow? workflow = await _coreUnitOfWork.Repository<Workflow>()
            .GetFirstOrDefaultAsync(workflowSpec, cancellationToken);

        if (workflow is null)
        {
            return Error.Conflict(
                ERROR_SIGNATURES_STALE,
                $"Workflow of type '{pending.WorkflowTypeId}' no longer exists — the pending override cannot be promoted.");
        }

        Dictionary<string, List<Block>> references = workflow.GetSignatureFieldReferences();
        if (!references.TryGetValue(pending.PlaceholderName, out List<Block>? referencingBlocks)
            || referencingBlocks.Count == 0)
        {
            return Error.Conflict(
                ERROR_SIGNATURES_STALE,
                $"Signature field '{pending.PlaceholderName}' is no longer defined and referenced on this workflow — the pending override cannot be promoted.");
        }

        string blockNamesJson = SignatureOverrideWriter.SerializeBlockNames(
            referencingBlocks.Select(b => b.Name));

        List<string> approverIds = [];
        foreach (SignatureOverrideVote v in pending.Votes)
        {
            if (v.VoteKind is SignatureVoteKind.Approve)
            {
                approverIds.Add(v.VoterUserId);
            }
        }

        List<string> idsToResolve = [pending.ProposerUserId, .. approverIds];
        Dictionary<string, string?> emails = await SignatureActorResolver.ResolveEmailsAsync(
            _userDataUnitOfWork,
            idsToResolve,
            cancellationToken);

        emails.TryGetValue(pending.ProposerUserId, out string? proposerEmail);

        List<SignatureAuditApprover> approvers = [];
        foreach (string id in approverIds)
        {
            emails.TryGetValue(id, out string? email);
            approvers.Add(new SignatureAuditApprover(id, email));
        }

        await SignatureOverrideWriter.ApplyUpdatesAsync(
            pending.OrganizationId,
            pending.WorkflowTypeId,
            pending.ProposerUserId,
            proposerEmail,
            approvers,
            new[] { new SignatureFieldUpdate(pending.PlaceholderName, pending.ProposedValue, blockNamesJson) },
            _coreUnitOfWork,
            cancellationToken);

        return Result.Success;
    }

    private static bool IsUniqueVoteViolation(DbUpdateException ex)
    {
        // Walk the inner-exception chain looking for the provider-specific
        // "unique constraint violated" error code. Matching on the entity type in
        // ex.Entries alone would mis-classify FK / NULL / length failures on the
        // vote row as "already voted" — e.g. a concurrent Cancel of the pending
        // leaves this handler's insert with a dangling FK that EF still reports
        // with the vote entity attached.
        //
        // IniaAi.Application doesn't reference Npgsql or Microsoft.Data.SqlClient
        // directly (layering: providers live in Infrastructure), so we match on
        // the exception's type name and read the error code reflectively.
        //   PostgreSQL: SQLSTATE 23505  (Npgsql.PostgresException.SqlState)
        //   SQL Server: 2627 / 2601      (Microsoft.Data.SqlClient.SqlException.Number)
        const string PG_UNIQUE_VIOLATION = "23505";

        for (Exception? current = ex.InnerException; current is not null; current = current.InnerException)
        {
            Type type = current.GetType();
            string typeName = type.FullName ?? string.Empty;

            if (typeName == "Npgsql.PostgresException")
            {
                string? sqlState = type.GetProperty("SqlState")?.GetValue(current) as string;
                if (sqlState == PG_UNIQUE_VIOLATION)
                {
                    return true;
                }
            }
            else if (typeName is "Microsoft.Data.SqlClient.SqlException"
                or "System.Data.SqlClient.SqlException")
            {
                if (type.GetProperty("Number")?.GetValue(current) is int number
                    && (number == 2627 || number == 2601))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
