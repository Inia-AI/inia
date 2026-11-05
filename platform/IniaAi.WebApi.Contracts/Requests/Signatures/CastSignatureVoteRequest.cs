using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.Signatures;

/// <summary>
/// Payload for POST /api/v1/workflow-types/{workflowTypeId}/signatures/pending/{pendingOverrideId}/votes.
/// Casts a reviewer's vote on a pending signature override. Rejections may carry
/// a free-form <see cref="Comment"/> and an optional <see cref="SuggestedValue"/>
/// that the proposer can later accept via accept-suggestion.
/// </summary>
public record CastSignatureVoteRequest(
    SignatureVoteKind VoteKind,
    string? Comment,
    string? SuggestedValue);
