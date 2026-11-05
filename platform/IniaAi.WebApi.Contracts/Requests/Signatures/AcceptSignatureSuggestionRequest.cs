namespace IniaAi.WebApi.Contracts.Requests.Signatures;

/// <summary>
/// Payload for POST /api/v1/workflow-types/{workflowTypeId}/signatures/pending/{pendingOverrideId}/accept-suggestion.
/// The original proposer accepts one of the suggested replacement values carried on
/// a rejection vote; the pending's <c>ProposedValue</c> is replaced with the
/// suggestion and all votes are reset (proposer auto-approve re-seeded).
/// </summary>
public record AcceptSignatureSuggestionRequest(string VoteId);
