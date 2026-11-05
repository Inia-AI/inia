using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace IniaAi.Application.Commands.WorkflowExecutions.SubmitWorkflowExecution;

/// <summary>
/// Command to submit a workflow execution with files and metadata in a single request.
/// Files are uploaded internally and the workflow is started.
/// </summary>
/// <param name="OrganizationId">The organization ID of the current user.</param>
/// <param name="UserId">The ID of the user submitting the workflow.</param>
/// <param name="Username">The username of the user submitting the workflow.</param>
/// <param name="Email">The email of the user submitting the workflow.</param>
/// <param name="Metadata">The JSON metadata describing the workflow and file mappings.</param>
/// <param name="Files">The binary files to upload, matched to Metadata.Files by array index.</param>
/// <param name="IdempotencyKey">Optional idempotency key to prevent duplicate submissions.</param>
/// <param name="Submitter">The signed-in user, so the organization's upload limits can leave out the platform's staff.</param>
public record SubmitWorkflowExecutionCommand(
    string OrganizationId,
    string UserId,
    string? Username,
    string? Email,
    SubmitWorkflowExecutionRequest Metadata,
    IReadOnlyList<IFormFile> Files,
    string? IdempotencyKey = null,
    ClaimsPrincipal? Submitter = null) : IRequest<ErrorOr<WorkflowExecutionDto>>
{
    public override string ToString()
    {
        return $"{nameof(SubmitWorkflowExecutionCommand)} {{ OrganizationId = {OrganizationId}, UserId = {UserId}, "
            + $"WorkflowTypeId = {Metadata.WorkflowTypeId}, Files = {Files.Count}, IdempotencyKey = {IdempotencyKey}, "
            + $"FreezeSourceExecutionId = {Metadata.FreezeSourceExecutionId}, FreezeSourceCaseId = {Metadata.FreezeSourceCaseId} }}";
    }
}
