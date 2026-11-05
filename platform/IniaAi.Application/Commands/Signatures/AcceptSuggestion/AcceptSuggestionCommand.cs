using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.Signatures.AcceptSuggestion;

/// <summary>
/// The original proposer accepts a reviewer's suggested alternative value
/// carried on a rejection vote. On accept the pending's
/// <c>ProposedValue</c> is replaced with the suggestion, all votes are cleared
/// (including any other rejections), and a fresh proposer auto-approve vote is
/// seeded so the normal review cycle restarts.
/// </summary>
public record AcceptSuggestionCommand(
    string OrganizationId,
    string UserId,
    string PendingOverrideId,
    string VoteId) : IRequest<ErrorOr<Success>>;
