using ErrorOr;
using IniaAi.Application.Commands.Signatures.AcceptSuggestion;
using IniaAi.Application.Commands.Signatures.CancelPendingSignatureOverride;
using IniaAi.Application.Commands.Signatures.CastSignatureOverrideVote;
using IniaAi.Application.Commands.Signatures.SubmitPendingSignatureOverride;
using IniaAi.Application.Commands.Signatures.WithdrawSignatureOverrideVote;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Signatures.GetSignatureHistory;
using IniaAi.Application.Queries.Signatures.GetWorkflowSignatures;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests.Signatures;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Exposes per-workflow signature fields: the staff-authored defaults layered with
/// client-authored override values. The legacy <c>PUT</c> endpoint is deprecated —
/// all write paths now flow through the pending / vote / accept-suggestion review
/// queue exposed by the <c>/pending</c> sub-routes. Access is restricted to the
/// SuperAdmin and SignatureContributor roles (class-level authorize below).
///
/// <para>
/// The route is scoped by <c>workflowTypeId</c> rather than the Core workflow GUID so
/// the URL survives Save-As-Core id regeneration. The vote/accept payloads still
/// carry block/pending GUIDs from the client's most recent GET — the handlers
/// return <see cref="StatusCodes.Status409Conflict"/> if any of those ids have
/// since been invalidated.
/// </para>
/// </summary>
[Route("api/v{version:apiVersion}/workflow-types/{workflowTypeId}/signatures")]
[Authorize(Roles = $"{Roles.SUPER_ADMIN},{Roles.SIGNATURE_CONTRIBUTOR}")]
[RequiresCapability(OrganizationCapabilities.SIGNATURES)]
public class SignaturesController : ApiController
{
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignaturesController"/> class.
    /// </summary>
    public SignaturesController(
        IRequestContext requestContext,
        ISender sender)
        : base(requestContext)
    {
        _sender = sender;
    }

    /// <summary>
    /// Returns the full signature surface for a workflow type — every section, every AI Query
    /// block that declares signature fields, merged with any client overrides. Fields also carry
    /// the active <see cref="PendingSignatureOverrideDto"/> when one is waiting review.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowSignaturesDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<WorkflowSignaturesDto>> GetSignatures(string workflowTypeId)
    {
        var query = new GetWorkflowSignaturesQuery(OrganizationId, UserId, workflowTypeId);
        ErrorOr<WorkflowSignaturesDto> result = await _sender.Send(query);

        return result.Match<ActionResult<WorkflowSignaturesDto>>(
            signatures => signatures,
            errors => Problem(errors));
    }

    /// <summary>
    /// Returns one page of audit-log entries for a workflow's signature override value
    /// transitions. Filters are optional; results are ordered newest first.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SignatureHistoryPageDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SignatureHistoryPageDto>> GetHistory(
        string workflowTypeId,
        [FromQuery] string? placeholderName = null,
        [FromQuery] string? actorUserId = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        // Convert to UTC DateTime explicitly. Accepting DateTimeOffset on the wire
        // forces clients to send a timezone-aware value (or ISO 8601 with offset)
        // rather than a bare "2026-01-01" that ASP.NET would bind as
        // DateTimeKind.Unspecified, which then compares ambiguously against the
        // UTC CreatedAt column.
        var query = new GetSignatureHistoryQuery(
            OrganizationId,
            UserId,
            workflowTypeId,
            placeholderName,
            actorUserId,
            fromUtc?.UtcDateTime,
            toUtc?.UtcDateTime,
            skip,
            take);

        ErrorOr<SignatureHistoryPageDto> result = await _sender.Send(query);

        return result.Match<ActionResult<SignatureHistoryPageDto>>(
            page => page,
            errors => Problem(errors));
    }

    /// <summary>
    /// Submits a new client-proposed signature override for reviewer voting. The proposer
    /// is automatically seeded as an Approve vote so the tenant threshold counts it. An
    /// existing pending for the same <c>(block, placeholder)</c> is replaced (and its votes
    /// cleared) on new submission. A <see langword="null"/> <c>ProposedValue</c> means
    /// "remove the current active override".
    /// </summary>
    [HttpPost("pending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> SubmitPending(
        string workflowTypeId,
        [FromBody] SubmitPendingSignatureRequest request)
    {
        var command = new SubmitPendingSignatureOverrideCommand(
            OrganizationId,
            UserId,
            workflowTypeId,
            request.PlaceholderName,
            request.ProposedValue);

        ErrorOr<string> result = await _sender.Send(command);

        return result.Match<ActionResult>(
            pendingOverrideId => Ok(new { pendingOverrideId }),
            errors => Problem(errors));
    }

    /// <summary>
    /// Casts a reviewer's vote on a pending signature override. When the approve count
    /// reaches the tenant's <c>SignatureReviewThreshold</c> the proposed value is promoted
    /// onto the target block and the pending is removed; when the reject count reaches
    /// the threshold the pending is removed without changing the active value. The
    /// proposer is already counted as an auto-Approve and cannot cast further votes
    /// (<c>Signatures.ProposerCannotVote</c>); the same voter cannot vote twice
    /// (<c>Signatures.AlreadyVoted</c>).
    /// </summary>
    [HttpPost("pending/{pendingOverrideId}/votes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> CastVote(
        string workflowTypeId,
        string pendingOverrideId,
        [FromBody] CastSignatureVoteRequest request)
    {
        var command = new CastSignatureOverrideVoteCommand(
            OrganizationId,
            UserId,
            pendingOverrideId,
            (SignatureVoteKind)request.VoteKind,
            request.Comment,
            request.SuggestedValue);

        ErrorOr<Success> result = await _sender.Send(command);

        return result.Match<ActionResult>(
            _ => Ok(),
            errors => Problem(errors));
    }

    /// <summary>
    /// The original proposer accepts a suggested replacement value carried on a rejection
    /// vote. The pending's <c>ProposedValue</c> is replaced with the suggestion, all
    /// votes are cleared, and a fresh proposer auto-Approve is seeded so the normal
    /// review cycle restarts. Only the proposer of the pending may call this
    /// (<c>Signatures.NotProposer</c>).
    /// </summary>
    [HttpPost("pending/{pendingOverrideId}/accept-suggestion")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> AcceptSuggestion(
        string workflowTypeId,
        string pendingOverrideId,
        [FromBody] AcceptSignatureSuggestionRequest request)
    {
        var command = new AcceptSuggestionCommand(
            OrganizationId,
            UserId,
            pendingOverrideId,
            request.VoteId);

        ErrorOr<Success> result = await _sender.Send(command);

        return result.Match<ActionResult>(
            _ => Ok(),
            errors => Problem(errors));
    }

    /// <summary>
    /// A reviewer withdraws their own vote on a pending override so they can re-cast or
    /// leave it unvoted for another reviewer to break the tie. Proposer's structural
    /// auto-approve cannot be withdrawn (<c>Signatures.ProposerCannotWithdraw</c>); the
    /// proposer should cancel the pending instead.
    /// </summary>
    [HttpDelete("pending/{pendingOverrideId}/votes/mine")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> WithdrawVote(
        string workflowTypeId,
        string pendingOverrideId)
    {
        var command = new WithdrawSignatureOverrideVoteCommand(
            OrganizationId,
            UserId,
            pendingOverrideId);

        ErrorOr<Success> result = await _sender.Send(command);

        return result.Match<ActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// The original proposer withdraws their pending override before the threshold is
    /// reached. Deletes the pending and cascades the votes. Intended as an escape hatch
    /// for threshold-vs-user-count stalemates (e.g. 1 approve + 1 reject on threshold 2).
    /// Only the proposer may call this (<c>Signatures.NotProposer</c>).
    /// </summary>
    [HttpDelete("pending/{pendingOverrideId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> CancelPending(
        string workflowTypeId,
        string pendingOverrideId)
    {
        var command = new CancelPendingSignatureOverrideCommand(
            OrganizationId,
            UserId,
            pendingOverrideId);

        ErrorOr<Success> result = await _sender.Send(command);

        return result.Match<ActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }
}
