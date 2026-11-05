using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.Signatures.WithdrawSignatureOverrideVote;

/// <summary>
/// A reviewer removes their own vote on a pending override so they can re-cast
/// (or so the tie can be broken by someone else). Proposer's auto-approve is
/// structural and cannot be withdrawn — proposer cancels the whole pending
/// instead. Identified by pending id + the caller's user id; no vote id needed
/// because the unique index <c>(PendingOverrideId, VoterUserId)</c> means each
/// voter owns at most one row per pending.
/// </summary>
public record WithdrawSignatureOverrideVoteCommand(
    string OrganizationId,
    string UserId,
    string PendingOverrideId) : IRequest<ErrorOr<Success>>;
