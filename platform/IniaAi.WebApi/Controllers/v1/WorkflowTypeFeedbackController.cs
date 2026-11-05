using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.ReportFeedback.ListWorkflowTypeFeedback;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.Application.Common.Dispatching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// The editor's view of report feedback: every organization's items on one workflow type, so the
/// people who tune the shared template see what its readers said. This is the one feedback read
/// that crosses the tenant boundary, by design, and the class-level gate keeps it to SuperAdmin:
/// an organization Admin can grant WorkflowEditor inside its own organization, so that role
/// cannot guard a read across organizations. Each item carries its organization id.
/// </summary>
[Route("api/v{version:apiVersion}/workflow-types/{workflowTypeId}/feedback")]
[Authorize(Roles = Roles.SUPER_ADMIN)]
public sealed class WorkflowTypeFeedbackController : ApiController
{
    private readonly ILogger<WorkflowTypeFeedbackController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowTypeFeedbackController"/> class.
    /// </summary>
    public WorkflowTypeFeedbackController(
        IRequestContext requestContext,
        ILogger<WorkflowTypeFeedbackController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Lists one page of every organization's feedback on a workflow type, newest first.
    /// </summary>
    /// <param name="workflowTypeId">The workflow type.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="take">Page size, at most 200.</param>
    /// <param name="cursor">The previous page's <c>nextCursor</c>; omit for the first page.</param>
    /// <returns>One page of feedback items.</returns>
    /// <response code="200">Page returned.</response>
    /// <response code="400">Invalid paging or filter values.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="403">User is not a SuperAdmin.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReportFeedbackPageDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> ListFeedback(
        string workflowTypeId,
        [FromQuery] ReportFeedbackStatusDto? status = null,
        [FromQuery] int take = 50,
        [FromQuery] string? cursor = null)
    {
        var query = new ListWorkflowTypeFeedbackQuery(
            OrganizationId,
            UserId,
            workflowTypeId,
            User.IsInRole(Roles.SUPER_ADMIN),
            status,
            take,
            cursor);

        ErrorOr<ReportFeedbackPageDto> result = await _sender.Send(query);

        _logger.LogInformation(
            "List workflow type feedback request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowTypeId: {WorkflowTypeId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(workflowTypeId),
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(Ok, Problem);
    }
}
