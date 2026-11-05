using System.Text.Json;
using ErrorOr;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// Orchestrates the shared logic for starting a workflow execution:
/// variable validation, document class validation, job creation, execution creation, and queueing.
/// </summary>
public interface IWorkflowExecutionOrchestrator
{
    /// <summary>
    /// Validates inputs, creates a job and workflow execution, and queues it for processing.
    /// </summary>
    /// <param name="workflow">The workflow definition to execute.</param>
    /// <param name="files">The uploaded file metadata (FileId + DocumentClass).</param>
    /// <param name="variables">Workflow variable values.</param>
    /// <param name="organizationId">The organization ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="username">The username.</param>
    /// <param name="email">The user email.</param>
    /// <param name="counterpartyId">Optional counterparty ID.</param>
    /// <param name="peerCompanyIds">Optional peer company IDs.</param>
    /// <param name="isConfidential">Whether the execution is confidential.</param>
    /// <param name="idempotencyKey">Optional idempotency key to prevent duplicate executions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="freezeSource">
    /// Where this execution's replayed external-fetch outputs come from, or null for a live run.
    /// One source by construction — see <see cref="FreezeSource"/>. Placed after
    /// <paramref name="ct"/> so existing positional callers keep binding the token.
    /// </param>
    /// <param name="triggerEmailIdentifier">
    /// Identifier of the email that triggered this execution (set when the submission expanded an
    /// email); lands in the job inputs under <c>SharedInputKeys.TRIGGER_EMAIL_IDENTIFIER</c>.
    /// </param>
    /// <param name="intakeSkippedFiles">
    /// Files the intake could not turn into documents; lands in the job inputs under
    /// <c>IntakeInputKeys.SKIPPED_PROTECTED_FILES</c> so the workflow handler writes Skipped log rows.
    /// </param>
    /// <param name="emailIntakeMode">
    /// How the email intake job stage should process the run's email documents (an
    /// <c>EmailIntakeModes</c> value); lands in the job inputs under
    /// <c>IntakeInputKeys.EMAIL_INTAKE_MODE</c>. Null (the /submit path, which expands emails
    /// before the job exists) leaves the stage inert.
    /// </param>
    /// <param name="emailAttachmentsClass">
    /// Document class for the files the intake stage extracts from emails; lands in the job
    /// inputs under <c>IntakeInputKeys.EMAIL_ATTACHMENTS_CLASS</c>.
    /// </param>
    /// <param name="isRerun">
    /// True for a validation act or a regression rerun. The job is then marked
    /// <c>IsRerun</c> and saves no change to live data. A submission that names a freeze source is
    /// a rerun as well, whatever this says.
    /// </param>
    Task<ErrorOr<WorkflowExecutionDto>> ExecuteWorkflowAsync(
        Workflow workflow,
        List<PostDocumentInfoRequest>? files,
        Dictionary<string, JsonElement>? variables,
        string organizationId,
        string userId,
        string? username,
        string? email,
        string? counterpartyId,
        List<string>? peerCompanyIds,
        bool isConfidential = false,
        string? idempotencyKey = null,
        CancellationToken ct = default,
        FreezeSource? freezeSource = null,
        string? triggerEmailIdentifier = null,
        IReadOnlyList<SkippedProtectedFile>? intakeSkippedFiles = null,
        string? emailIntakeMode = null,
        string? emailAttachmentsClass = null,
        bool isRerun = false);
}
