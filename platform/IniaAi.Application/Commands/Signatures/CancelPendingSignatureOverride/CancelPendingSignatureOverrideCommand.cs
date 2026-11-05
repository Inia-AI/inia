using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.Signatures.CancelPendingSignatureOverride;

/// <summary>
/// The original proposer withdraws their pending override before it reaches a
/// threshold of approves or rejects. Deletes the pending row (and cascades its
/// votes). Intended escape hatch for the threshold-vs-user-count stalemate:
/// 1 proposer auto-approve + 1 reviewer reject in a two-person org sits forever
/// because neither side can reach threshold. Proposer-only; not available to
/// reviewers.
/// </summary>
public record CancelPendingSignatureOverrideCommand(
    string OrganizationId,
    string UserId,
    string PendingOverrideId) : IRequest<ErrorOr<Success>>;
