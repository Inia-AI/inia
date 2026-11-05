using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Persistence.UserData.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Signatures.WithdrawSignatureOverrideVote;

/// <summary>
/// Removes the caller's own vote from a pending override. Proposer's structural
/// auto-approve cannot be withdrawn (<c>Signatures.ProposerCannotWithdraw</c>);
/// a <c>NotFound</c> on the vote means the caller never voted on this pending.
/// Does not re-evaluate thresholds — withdrawing can only reduce counts, never
/// cross a threshold upward, so no promotion / cancellation is possible.
/// </summary>
public class WithdrawSignatureOverrideVoteCommandHandler
    : IRequestHandler<WithdrawSignatureOverrideVoteCommand, ErrorOr<Success>>
{
    private const string ERROR_PENDING_NOT_FOUND = "PendingSignatureOverride.NotFound";
    private const string ERROR_VOTE_NOT_FOUND = "SignatureOverrideVote.NotFound";
    private const string ERROR_PROPOSER_CANNOT_WITHDRAW = "Signatures.ProposerCannotWithdraw";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ILogger<WithdrawSignatureOverrideVoteCommandHandler> _logger;

    public WithdrawSignatureOverrideVoteCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ILogger<WithdrawSignatureOverrideVoteCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<Success>> Handle(
        WithdrawSignatureOverrideVoteCommand request,
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
                ERROR_PROPOSER_CANNOT_WITHDRAW,
                "The proposer cannot withdraw their structural auto-approve. Cancel the pending override instead.");
        }

        SignatureOverrideVote? vote = null;
        foreach (SignatureOverrideVote v in pending.Votes)
        {
            if (v.VoterUserId == request.UserId)
            {
                vote = v;
                break;
            }
        }

        if (vote is null)
        {
            return Error.NotFound(
                ERROR_VOTE_NOT_FOUND,
                "You have no vote to withdraw on this pending override.");
        }

        _coreUnitOfWork.Repository<SignatureOverrideVote>().HardRemove(vote);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} withdrew {VoteKind} vote on pending {PendingId}",
            request.UserId,
            vote.VoteKind,
            pending.Id);

        return Result.Success;
    }
}
