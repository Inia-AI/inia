namespace IniaAi.WebApi.Contracts.Requests.Signatures;

/// <summary>
/// Payload for POST /api/v1/workflow-types/{workflowTypeId}/signatures/pending.
/// Submits a new client-proposed override for reviewer voting, keyed by the
/// workflow-level field name. The proposer is automatically recorded as an Approve
/// vote. A <see langword="null"/> <see cref="ProposedValue"/> means "remove the
/// current active override and fall back to the staff-authored default".
/// </summary>
public record SubmitPendingSignatureRequest(
    string PlaceholderName,
    string? ProposedValue);
