using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
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

namespace IniaAi.Application.Commands.Signatures.AcceptSuggestion;

/// <summary>
/// Proposer-only flow: copies a reviewer's <see cref="SignatureOverrideVote.SuggestedValue"/>
/// onto the pending's <c>ProposedValue</c>, clears every existing vote, and seeds fresh
/// Approve votes for both the proposer (they just accepted) and the suggester (they
/// proposed this exact value on their reject vote, so implicit approval is fair). If
/// the combined seed already meets the tenant threshold, the pending is promoted to an
/// active override in the same unit of work.
/// </summary>
public class AcceptSuggestionCommandHandler
    : IRequestHandler<AcceptSuggestionCommand, ErrorOr<Success>>
{
    private const string ERROR_PENDING_NOT_FOUND = "PendingSignatureOverride.NotFound";
    private const string ERROR_VOTE_NOT_FOUND = "SignatureOverrideVote.NotFound";
    private const string ERROR_NOT_PROPOSER = "Signatures.NotProposer";
    private const string ERROR_NO_SUGGESTED_VALUE = "Signatures.NoSuggestedValue";
    private const string ERROR_SIGNATURES_STALE = "Signatures.Stale";
    private const int MIN_THRESHOLD = 2;

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ILogger<AcceptSuggestionCommandHandler> _logger;

    public AcceptSuggestionCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IUserDataUnitOfWork userDataUnitOfWork,
        ILogger<AcceptSuggestionCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<Success>> Handle(
        AcceptSuggestionCommand request,
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

        if (pending.ProposerUserId != request.UserId)
        {
            return Error.Forbidden(
                ERROR_NOT_PROPOSER,
                "Only the original proposer can accept a suggestion.");
        }

        SignatureOverrideVote? vote = null;
        foreach (SignatureOverrideVote v in pending.Votes)
        {
            if (v.Id == request.VoteId)
            {
                vote = v;
                break;
            }
        }

        if (vote is null)
        {
            return Error.NotFound(
                ERROR_VOTE_NOT_FOUND,
                $"Vote '{request.VoteId}' was not found on pending override '{request.PendingOverrideId}'.");
        }

        if (vote.SuggestedValue is null)
        {
            return Error.Validation(
                ERROR_NO_SUGGESTED_VALUE,
                "The selected vote does not carry a suggested value to accept.");
        }

        pending.ProposedValue = vote.SuggestedValue;
        string suggesterUserId = vote.VoterUserId;

        IRepository<SignatureOverrideVote> voteRepo =
            _coreUnitOfWork.Repository<SignatureOverrideVote>();

        foreach (SignatureOverrideVote existing in pending.Votes.ToList())
        {
            voteRepo.HardRemove(existing);
        }

        pending.Votes.Clear();

        var proposerApprove = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = request.UserId,
            VoteKind = SignatureVoteKind.Approve,
        };

        await voteRepo.AddAsync(proposerApprove);
        pending.Votes.Add(proposerApprove);

        int approveCount = 1;

        // Proposer can't vote during normal review, so the suggester is always a
        // different user; guard anyway so a future rule change can't double-seed.
        if (suggesterUserId != request.UserId)
        {
            var suggesterApprove = new SignatureOverrideVote
            {
                PendingOverrideId = pending.Id,
                VoterUserId = suggesterUserId,
                VoteKind = SignatureVoteKind.Approve,
            };

            await voteRepo.AddAsync(suggesterApprove);
            pending.Votes.Add(suggesterApprove);
            approveCount = 2;
        }

        int threshold = await LoadTenantThresholdAsync(request.OrganizationId, cancellationToken);

        if (approveCount >= threshold)
        {
            ErrorOr<Success> promoteResult = await PromoteAsync(pending, cancellationToken);
            if (promoteResult.IsError)
            {
                return promoteResult;
            }

            pendingRepo.HardRemove(pending);
        }

        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "Proposer {UserId} accepted suggestion from vote {VoteId} on pending {PendingId}; approves={ApproveCount} threshold={Threshold}",
            request.UserId,
            LogSanitizer.ForLog(request.VoteId),
            pending.Id,
            approveCount,
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
            return MIN_THRESHOLD;
        }

        return organization.SignatureReviewThreshold;
    }

    // Duplicated from CastSignatureOverrideVoteCommandHandler — both post-vote-mutation
    // paths need the same "find block + write override" sequence. If a third caller
    // appears, extract to a shared helper class.
    private async Task<ErrorOr<Success>> PromoteAsync(
        PendingSignatureOverride pending,
        CancellationToken cancellationToken)
    {
        // Workflow rows are shared across tenants; the pending row's OrganizationId
        // match is enforced upstream so no further org-scoped filter is needed here.
        // Spec applies OrderByDescending(CreatedAt) to align with the workflow
        // execution path's row resolution.
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
}
