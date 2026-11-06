using ErrorOr;
using IniaAi.Application.Commands.ReportFeedback.CreateReportFeedback;
using IniaAi.Application.Commands.ReportFeedback.DeleteReportFeedback;
using IniaAi.Application.Commands.ReportFeedback.UpdateReportFeedbackStatus;
using IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution;
using IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution.Models;
using IniaAi.Application.Commands.WorkflowReports.DeleteReportParagraph;
using IniaAi.Application.Commands.WorkflowReports.DeleteReportSection;
using IniaAi.Application.Commands.WorkflowReports.UpdateReportParagraph;
using IniaAi.Application.Commands.WorkflowReports.UpdateReportSection;
using IniaAi.Application.Commands.WorkflowReports.DeleteWorkflowReport;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutions;
using IniaAi.Application.Queries.ReportFeedback.ListReportFeedback;
using IniaAi.Application.Queries.TokenUsage.GetExecutionCost;
using IniaAi.Application.Queries.WorkflowReports.ListReportDataOutputs;
using IniaAi.Application.Queries.WorkflowReports.ListReportSections;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests.ReportFeedback;
using IniaAi.WebApi.Contracts.Requests.WorkflowReports;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders;
using IniaAi.WebApi.Models.Enums;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides endpoints for managing workflows and launching workflow jobs.
/// </summary>
[Route("api/v{version:apiVersion}/workflow-reports")]
public sealed class WorkflowReportsController : ApiController
{
    private readonly ILogger<WorkflowReportsController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowReportsController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">Logger for recording operational events.</param>
    /// <param name="sender">Sends each request to its handler.</param>
    public WorkflowReportsController(
        IRequestContext requestContext,
        ILogger<WorkflowReportsController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Retrieves a workflow output by its ID.
    /// </summary>
    /// <remarks>
    /// Returns a single workflow execution if found.
    /// Otherwise, returns a 404 error.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <returns>The workflow output if found.</returns>
    /// <response code="200">Workflow execution found and returned successfully.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Forbidden request.</response>
    /// <response code="404">Workflow output not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(WorkflowExecutionDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(string id)
    {
        ErrorOr<WorkflowExecutionDto> result = await _sender.Send(new GetWorkflowExecutionQuery(OrganizationId, UserId, id));

        _logger.LogInformation(
            "Get workflow execution by ID request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves all available workflow outputs for the current user's organization. (filtered)
    /// </summary>
    /// <remarks>
    /// Supports OData filtering, ordering, and pagination.
    /// <br/><br/>
    /// You can apply OData query parameters like:
    /// <ul>
    /// <li><c>$filter</c> � filter by properties (e.g. <c>type eq 'workflow'</c>)</li>
    /// <li><c>$orderby</c> � sort results (e.g. <c>createdAt desc</c>)</li>
    /// <li><c>$top</c> � limit number of results</li>
    /// <li><c>$count=true</c> � include total count</li>
    /// </ul>
    /// </remarks>
    /// <param name="queryOptions">The OData query options (filter, order, skip, top, etc.).</param>
    /// <returns>A paged and filterable list of workflow outputs with OData metadata.</returns>
    /// <response code="200">Workflow outputs retrieved successfully.</response>
    /// <response code="400">User organization is missing or invalid.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpGet]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<WorkflowExecutionSummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ODataPaginatedResponse<WorkflowExecutionSummaryDto>>> Get(ODataQueryOptions<WorkflowExecutionSummaryDto> queryOptions)
    {
        var query = new ListWorkflowExecutionsQuery(OrganizationId, UserId, queryOptions);
        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result = await _sender.Send(query);

        _logger.LogInformation(
            "Get workflow executions request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, Query: {Query}, Success: {Success}",
            OrganizationId,
            UserId,
            queryOptions.RawValues,
            result.IsError ? "No" : "Yes");

        return result.Match<ActionResult<ODataPaginatedResponse<WorkflowExecutionSummaryDto>>>(
            pageResult => { return pageResult; },
            error => Problem());
    }

    /// <summary>
    /// Retrieves workflow report elements by execution ID.
    /// </summary>
    /// <remarks>
    /// Returns a list of workflow report elements if found.
    /// Otherwise, returns a 404 error.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <returns>The workflow report elements if found.</returns>
    /// <response code="200">Workflow report elements found and returned successfully.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Forbidden request.</response>
    /// <response code="404">Workflow report elements not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("{id}/elements")]
    [ReportContent]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ListResponse<ReportSectionDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetReportSections(string id)
    {
        ErrorOr<List<ReportSectionDto>> result = await _sender.Send(
            new ListReportSectionsQuery(
                id,
                OrganizationId));

        _logger.LogInformation(
            "Get workflow report elements request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(
             value => Ok(new ListResponse<ReportSectionDto>(value)),
             Problem);
    }

    /// <summary>
    /// Retrieves the JSON data outputs a run returned beside its report.
    /// </summary>
    /// <remarks>
    /// Each output is one block's JSON, named by the workflow, returned as a JSON value. Its
    /// citations name the value each cited figure sits in by JSON Pointer. An execution with no
    /// report, or one of another organization, returns an empty list.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <returns>The run's data outputs, ordered by name.</returns>
    /// <response code="200">Data outputs returned.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Forbidden request.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("{id}/data")]
    [ReportContent]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ListResponse<ReportDataOutputDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetReportDataOutputs(string id)
    {
        ErrorOr<List<ReportDataOutputDto>> result = await _sender.Send(
            new ListReportDataOutputsQuery(
                id,
                OrganizationId));

        _logger.LogInformation(
            "Get workflow report data outputs request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(
             value => Ok(new ListResponse<ReportDataOutputDto>(value)),
             Problem);
    }

    /// <summary>
    /// Returns what one report's run cost: the total and a line per block (name, models, tokens,
    /// cost), read from the run's stored usage rows.
    /// </summary>
    /// <remarks>
    /// A super admin reads any report in the organization. The organization's service user reads
    /// the reports it started, so a scripted test run can report its own cost. Any other caller
    /// gets 403, and a report in another organization answers 404.
    /// </remarks>
    /// <param name="id">The unique identifier of the workflow execution.</param>
    /// <returns>The run's cost.</returns>
    /// <response code="200">Cost returned.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">The caller may not read this run's cost.</response>
    /// <response code="404">Workflow execution not found.</response>
    [HttpGet("{id}/cost")]
    [RequiresCapability(OrganizationCapabilities.RUN_EVIDENCE)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ExecutionCostDto))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    public async Task<IActionResult> GetCost(string id)
    {
        ErrorOr<ExecutionCostDto> result = await _sender.Send(
            new GetExecutionCostQuery(OrganizationId, UserId, id)
            {
                CallerIsSuperAdmin = User.IsInRole(Roles.SUPER_ADMIN),
            });

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Updates the editable fields of a report section.
    /// </summary>
    /// <param name="id">The ID of the workflow execution.</param>
    /// <param name="sectionId">The ID of the report section to update.</param>
    /// <param name="request">The updated section fields.</param>
    /// <returns>No content if the update is successful.</returns>
    /// <response code="204">Successfully updated.</response>
    /// <response code="400">Invalid request body.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="403">User does not have permission to update.</response>
    /// <response code="404">Report section not found.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPut("{id}/sections/{sectionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> UpdateReportSection(string id, string sectionId, [FromBody] UpdateReportSectionRequest request)
    {
        var command = new UpdateReportSectionCommand(OrganizationId, UserId, id, sectionId, request);

        ErrorOr<Success> result = await _sender.Send(command);

        _logger.LogInformation(
            "Update report section request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, SectionId: {SectionId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            LogSanitizer.ForLog(sectionId),
            result.IsError ? "No" : "Yes");

        return result.Match(
         _ => NoContent(),
         Problem);
    }

    /// <summary>
    /// Updates the editable fields of a report paragraph.
    /// </summary>
    /// <param name="id">The ID of the workflow execution.</param>
    /// <param name="paragraphId">The ID of the report paragraph to update.</param>
    /// <param name="request">The updated paragraph fields.</param>
    /// <returns>No content if the update is successful.</returns>
    /// <response code="204">Successfully updated.</response>
    /// <response code="400">Invalid request body.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="403">User does not have permission to update.</response>
    /// <response code="404">Report paragraph not found.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPut("{id}/paragraphs/{paragraphId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> UpdateReportParagraph(string id, string paragraphId, [FromBody] UpdateReportParagraphRequest request)
    {
        var command = new UpdateReportParagraphCommand(OrganizationId, UserId, id, paragraphId, request);

        ErrorOr<Success> result = await _sender.Send(command);

        _logger.LogInformation(
            "Update report paragraph request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, ParagraphId: {ParagraphId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            LogSanitizer.ForLog(paragraphId),
            result.IsError ? "No" : "Yes");

        return result.Match(
         _ => NoContent(),
         Problem);
    }

    /// <summary>
    /// Deletes a report section and its paragraphs.
    /// </summary>
    /// <param name="id">The ID of the workflow execution.</param>
    /// <param name="sectionId">The ID of the report section to delete.</param>
    /// <returns>No content if deletion is successful.</returns>
    /// <response code="204">Deleted successfully.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="403">User does not have permission to delete.</response>
    /// <response code="404">Report section not found.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpDelete("{id}/sections/{sectionId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    [SwaggerResponseExample(StatusCodes.Status400BadRequest, typeof(BadRequestErrorExample))]
    [SwaggerResponseExample(StatusCodes.Status404NotFound, typeof(NotFoundErrorExample))]
    public async Task<IActionResult> DeleteReportSection(string id, string sectionId)
    {
        var command = new DeleteReportSectionCommand(OrganizationId, UserId, id, sectionId);

        ErrorOr<Success> result = await _sender.Send(command);

        _logger.LogInformation(
            "Delete report section request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, SectionId: {SectionId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            LogSanitizer.ForLog(sectionId),
            result.IsError ? "No" : "Yes");

        return result.Match(
         _ => NoContent(),
         Problem);
    }

    /// <summary>
    /// Deletes a report paragraph.
    /// </summary>
    /// <param name="id">The ID of the workflow execution.</param>
    /// <param name="paragraphId">The ID of the report paragraph to delete.</param>
    /// <returns>No content if deletion is successful.</returns>
    /// <response code="204">Deleted successfully.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="403">User does not have permission to delete.</response>
    /// <response code="404">Report paragraph not found.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpDelete("{id}/paragraphs/{paragraphId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    [SwaggerResponseExample(StatusCodes.Status400BadRequest, typeof(BadRequestErrorExample))]
    [SwaggerResponseExample(StatusCodes.Status404NotFound, typeof(NotFoundErrorExample))]
    public async Task<IActionResult> DeleteReportParagraph(string id, string paragraphId)
    {
        var command = new DeleteReportParagraphCommand(OrganizationId, UserId, id, paragraphId);

        ErrorOr<Success> result = await _sender.Send(command);

        _logger.LogInformation(
            "Delete report paragraph request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, ParagraphId: {ParagraphId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            LogSanitizer.ForLog(paragraphId),
            result.IsError ? "No" : "Yes");

        return result.Match(
         _ => NoContent(),
         Problem);
    }

    /// <summary>
    /// Deletes a workflow report by ID.
    /// </summary>
    /// <param name="id">The ID of the workflow report to delete.</param>
    /// <returns>No content if deletion is successful.</returns>
    /// <response code="204">Deleted successfully.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="403">User does not have permission to delete.</response>
    /// <response code="404">Workflow report not found.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    [SwaggerResponseExample(StatusCodes.Status400BadRequest, typeof(BadRequestErrorExample))]
    [SwaggerResponseExample(StatusCodes.Status404NotFound, typeof(NotFoundErrorExample))]
    public async Task<IActionResult> DeleteById(string id)
    {
        var command = new DeleteWorkflowReportCommand(OrganizationId, UserId, id);

        ErrorOr<Success> result = await _sender.Send(command);

        _logger.LogInformation(
            "Delete workflow report request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            result.IsError ? "No" : "Yes");

        return result.Match(
         _ => NoContent(),
         Problem);
    }

    /// <summary>
    /// Records a reader's feedback on a report. The item belongs to the report as a whole; the
    /// reader's text selection, when any, is kept as quoted text. Any reader of the report may
    /// leave feedback.
    /// </summary>
    /// <param name="reportId">The ID of the workflow execution whose report is being commented on.</param>
    /// <param name="request">The feedback content.</param>
    /// <returns>The created feedback item.</returns>
    /// <response code="201">Feedback recorded.</response>
    /// <response code="400">Invalid request body.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="404">Workflow report not found in the caller's organization.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPost("{reportId}/feedback")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ReportFeedbackDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> CreateFeedback(string reportId, [FromBody] CreateReportFeedbackRequest request)
    {
        var command = new CreateReportFeedbackCommand(OrganizationId, UserId, reportId, request);

        ErrorOr<ReportFeedbackDto> result = await _sender.Send(command);

        _logger.LogInformation(
            "Create report feedback request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(reportId),
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(
            created => CreatedAtAction(nameof(ListFeedback), new { reportId }, created),
            Problem);
    }

    /// <summary>
    /// Lists the feedback left on a report. Org-scoped: a reader sees only the items of their own
    /// organization, with the status an editor gave each one.
    /// </summary>
    /// <param name="reportId">The ID of the workflow execution.</param>
    /// <returns>The feedback items on the report, oldest first.</returns>
    /// <response code="200">Feedback items returned.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("{reportId}/feedback")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ListResponse<ReportFeedbackDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> ListFeedback(string reportId)
    {
        ErrorOr<List<ReportFeedbackDto>> result = await _sender.Send(
            new ListReportFeedbackQuery(OrganizationId, UserId, reportId));

        _logger.LogInformation(
            "List report feedback request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(reportId),
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(
            items => Ok(new ListResponse<ReportFeedbackDto>(items)),
            Problem);
    }

    /// <summary>
    /// Moves a feedback item to a new status. Editor roles only; a dismissal carries its reason.
    /// </summary>
    /// <param name="reportId">The ID of the workflow execution.</param>
    /// <param name="feedbackId">The ID of the feedback item.</param>
    /// <param name="request">The new status and, when dismissing, the reason.</param>
    /// <returns>The updated feedback item.</returns>
    /// <response code="200">Status updated.</response>
    /// <response code="400">Invalid request body, or a dismissal without a reason.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="403">User does not hold an editor role.</response>
    /// <response code="404">Feedback item not found in the caller's organization.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPatch("{reportId}/feedback/{feedbackId}/status")]
    [Authorize(Roles = Roles.EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReportFeedbackDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> UpdateFeedbackStatus(string reportId, string feedbackId, [FromBody] UpdateReportFeedbackStatusRequest request)
    {
        var command = new UpdateReportFeedbackStatusCommand(OrganizationId, UserId, reportId, feedbackId, request);

        ErrorOr<ReportFeedbackDto> result = await _sender.Send(command);

        _logger.LogInformation(
            "Update report feedback status request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, FeedbackId: {FeedbackId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(reportId),
            LogSanitizer.ForLog(feedbackId),
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Deletes a feedback item for good, whatever its status. Any reader of the report may delete
    /// it. Org-scoped like the list: an item of another organization is not found.
    /// </summary>
    /// <param name="reportId">The ID of the workflow execution.</param>
    /// <param name="feedbackId">The ID of the feedback item.</param>
    /// <returns>No content when the item is deleted.</returns>
    /// <response code="204">Feedback item deleted.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="404">Feedback item not found in the caller's organization.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpDelete("{reportId}/feedback/{feedbackId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> DeleteFeedback(string reportId, string feedbackId)
    {
        ErrorOr<Deleted> result = await _sender.Send(
            new DeleteReportFeedbackCommand(OrganizationId, UserId, reportId, feedbackId));

        _logger.LogInformation(
            "Delete report feedback request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, FeedbackId: {FeedbackId}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(reportId),
            LogSanitizer.ForLog(feedbackId),
            result.IsError ? "No" : "Yes");

        return result.Match(
            _ => NoContent(),
            Problem);
    }

    /// <summary>
    /// Exports a workflow output by ID in the specified format.
    /// </summary>
    /// <remarks>
    /// The formats are <c>Word</c> (.docx) and <c>Markdown</c> (.md). Any other format is refused with 400.
    /// The file is returned with appropriate content headers and streamed directly to the client.
    /// </remarks>
    /// <param name="id">The workflow output ID.</param>
    /// <param name="format">The desired export format.</param>
    /// <returns>The exported document file, or an error response.</returns>
    /// <response code="200">Export successful. Returns the .docx or .md file as a stream.</response>
    /// <response code="400">Bad request. The ID format may be invalid or missing required input.</response>
    /// <response code="401">Unauthorized. The request is missing valid authentication credentials.</response>
    /// <response code="404">Not found. No workflow output was found for the specified ID, or it belongs to another organization.</response>
    /// <response code="500">Internal server error. An unexpected error occurred during export.</response>
    [HttpGet("{id}/export/{format}")]
    [ReportContent]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(List<Error>))]
    public async Task<IActionResult> Export(string id, ExportFormat format)
    {
        var command = new ExportWorkflowExecutionCommand(
            id,
            (Domain.Enums.ExportFormat)format,
            UserId,
            OrganizationId);

        ErrorOr<WorkflowExecutionExportResult> result = await _sender.Send(command);

        _logger.LogInformation(
            "Export workflow execution request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, WorkflowExecutionId: {WorkflowExecutionId}, ExportFormat: {ExportFormat}, Success: {Success}",
            OrganizationId,
            UserId,
            LogSanitizer.ForLog(id),
            format,
            result.IsError ? "No" : "Yes");

        return result.Match(
            exportResult => File(
                exportResult.Content.BinaryData.ToArray(),
                exportResult.ContentType,
                exportResult.FileName),
            Problem);
    }
}
