using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.Signatures.SubmitPendingSignatureOverride;

/// <summary>
/// Submits a new client-proposed signature-field override for reviewer voting.
/// The proposer is automatically recorded as an Approve vote on creation so the
/// tenant threshold counts it. Exactly one pending exists per
/// <c>(OrganizationId, WorkflowTypeId, PlaceholderName)</c> — an existing pending
/// for the same field is replaced (votes cleared via cascade) on new submission.
///
/// <para>
/// A <see langword="null"/> <see cref="ProposedValue"/> means the proposer is
/// asking to remove the current active override row so the staff default applies
/// again.
/// </para>
/// </summary>
public record SubmitPendingSignatureOverrideCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string PlaceholderName,
    string? ProposedValue) : IRequest<ErrorOr<string>>;
