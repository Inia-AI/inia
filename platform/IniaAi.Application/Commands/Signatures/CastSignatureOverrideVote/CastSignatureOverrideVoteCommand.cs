using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.Signatures;

namespace IniaAi.Application.Commands.Signatures.CastSignatureOverrideVote;

/// <summary>
/// Casts a reviewer's vote on a <see cref="PendingSignatureOverride"/>. When the
/// approve count reaches the tenant's <c>SignatureReviewThreshold</c> the
/// proposed value is promoted onto the target AI Query block and the pending
/// row is deleted. When the reject count reaches the threshold the pending row
/// is deleted without writing any override.
///
/// <para>
/// The original proposer is already counted as an auto-Approve vote and cannot
/// cast further votes (see <c>Signatures.ProposerCannotVote</c>).
/// A single voter cannot vote twice on the same pending (unique index) —
/// attempting to do so returns <c>Signatures.AlreadyVoted</c>.
/// </para>
/// </summary>
public record CastSignatureOverrideVoteCommand(
    string OrganizationId,
    string UserId,
    string PendingOverrideId,
    SignatureVoteKind VoteKind,
    string? Comment,
    string? SuggestedValue) : IRequest<ErrorOr<Success>>;
