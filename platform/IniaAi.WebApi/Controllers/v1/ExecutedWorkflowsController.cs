using ErrorOr;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Responses.ExecutedWorkflows;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Endpoints for downloading executed workflow snapshots as JSON.
/// Mirrors the WebApp <c>UserWorkflows/Pages/Workflows.razor</c> download
/// behavior for use outside the Blazor app (scripts, diagnostics, regression seeding).
/// An operator or super admin reads any run in the organization; the organization's service
/// user reads the runs it started, so a scripted test run can read its own snapshot.
/// </summary>
[Route("api/v{version:apiVersion}/executed-workflows")]
public sealed class ExecutedWorkflowsController : ApiController
{
    private readonly ILogger<ExecutedWorkflowsController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutedWorkflowsController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sender">The sender instance.</param>
    public ExecutedWorkflowsController(
        IRequestContext requestContext,
        ILogger<ExecutedWorkflowsController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Downloads an executed workflow snapshot as a pretty-printed JSON
    /// <c>.workflow</c> file. Exactly one of <paramref name="executionId"/>,
    /// <paramref name="workflowId"/>, or <paramref name="jobId"/> must be supplied.
    /// </summary>
    /// <param name="executionId">Optional WorkflowExecution.Id.</param>
    /// <param name="workflowId">Optional Workflow.Id (the cloned execution snapshot).</param>
    /// <param name="jobId">Optional Hangfire JobId attached to the execution.</param>
    /// <returns>The serialized workflow snapshot as a downloadable file.</returns>
    [HttpGet("download")]
    [RequiresCapability(OrganizationCapabilities.RUN_EVIDENCE)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    public async Task<IActionResult> Download(
        [FromQuery] string? executionId,
        [FromQuery] string? workflowId,
        [FromQuery] string? jobId)
    {
        var query = new DownloadExecutedWorkflowQuery(
            OrganizationId,
            UserId,
            executionId,
            workflowId,
            jobId)
        {
            CallerReadsEveryRun = Roles.HeldBy(User, Roles.OPERATOR_ROLES),
        };

        ErrorOr<DownloadExecutedWorkflowResult> result = await _sender.Send(query);

        _logger.LogInformation(
            "DownloadExecutedWorkflow processed for OrganizationId: {OrganizationId}, UserId: {UserId}, ExecutionId: {ExecutionId}, WorkflowId: {WorkflowId}, JobId: {JobId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(executionId),
            LogSanitizer.ForLog(workflowId),
            LogSanitizer.ForLog(jobId),
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(
            payload => File(payload.Content, payload.ContentType, payload.FileName),
            Problem);
    }
}
