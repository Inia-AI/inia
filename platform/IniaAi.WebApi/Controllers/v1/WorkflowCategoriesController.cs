using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Workflows.ListWorkflowCategories;
using IniaAi.Application.Queries.Workflows.ValidateDocumentClasses;
using IniaAi.Common.Text;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders.WorkflowOutput;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides endpoints for retrieving workflow categories and their associated workflows.
/// </summary>
[Route("api/v{version:apiVersion}/workflow-categories")]
public sealed class WorkflowCategoriesController : ApiController
{
    private readonly ILogger<WorkflowCategoriesController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowCategoriesController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">The logger instance for this controller.</param>
    /// <param name="sender">The sender for dispatching queries and commands.</param>
    public WorkflowCategoriesController(
        IRequestContext requestContext,
        ILogger<WorkflowCategoriesController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Retrieves the list of workflow categories available to the current user, with optional inclusion of workflows.
    /// </summary>
    /// <param name="includeWorkflows">If <c>true</c>, includes the list of workflows in each category.</param>
    /// <remarks>
    /// Each category groups workflows by business area (e.g., Asset Management, etc.).
    /// When includeWorkflows=true, returns full workflow details including Variables and DocumentClasses.
    /// For better performance on initial page load, use the /summaries endpoint instead.
    /// </remarks>
    /// <returns>A list of workflow categories with optional nested workflow details.</returns>
    /// <response code="200">Returns the list of workflow categories successfully.</response>
    /// <response code="204">No workflow categories are available to the user.</response>
    /// <response code="400">The request was invalid or could not be processed.</response>
    /// <response code="401">The user is not authenticated.</response>
    /// <response code="500">An internal server error occurred while processing the request.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ListResponse<WorkflowCategoryDto>))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ListWorkflowCategoriesResponseExample))]
    public async Task<IActionResult> GetWorkflowCategories([FromQuery] bool includeWorkflows = false)
    {
        ErrorOr<List<WorkflowCategoryDto>> result = await _sender.Send(new ListWorkflowCategoriesQuery(OrganizationId, UserId, includeWorkflows));

        _logger.LogInformation("GetWorkflowCategories - IncludeWorkflows: {IncludeWorkflows} - Result: {Result}",
            includeWorkflows,
            LogSanitizer.ForLog(result.IsError ? $"Error - {result.FirstError.Description}" : $"Success - {result.Value.Count} categories"));

        return result.Match<IActionResult>(
            value => Ok(new ListResponse<WorkflowCategoryDto>(value)),
            Problem);
    }

    /// <summary>
    /// Retrieves a lightweight list of workflow categories with basic workflow info (no Variables, DocumentClasses, etc.).
    /// </summary>
    /// <remarks>
    /// This endpoint is optimized for performance using SQL projection.
    /// Use this for initial page load - workflow names are included for immediate display.
    /// For full workflow details (Variables, DocumentClasses), use GET /workflow-templates/{workflowTypeId}/summary.
    /// Returns: Category (Id, Name, Description) and Workflow (Id, WorkflowTypeId, WorkflowTypeName, IsAvailable).
    /// </remarks>
    /// <returns>A lightweight list of workflow categories with basic workflow summaries.</returns>
    /// <response code="200">Returns the list of workflow category summaries successfully.</response>
    /// <response code="204">No workflow categories are available to the user.</response>
    /// <response code="400">The request was invalid or could not be processed.</response>
    /// <response code="401">The user is not authenticated.</response>
    /// <response code="500">An internal server error occurred while processing the request.</response>
    [HttpGet("summaries")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ListResponse<WorkflowCategorySummaryDto>))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetWorkflowCategorySummaries()
    {
        ErrorOr<List<WorkflowCategorySummaryDto>> result = await _sender.Send(new ListWorkflowCategorySummariesQuery(OrganizationId, UserId));

        _logger.LogInformation("GetWorkflowCategorySummaries - Result: {Result}",
            LogSanitizer.ForLog(result.IsError ? $"Error - {result.FirstError.Description}" : $"Success - {result.Value.Count} categories"));

        return result.Match<IActionResult>(
            value => Ok(new ListResponse<WorkflowCategorySummaryDto>(value)),
            Problem);
    }

    /// <summary>
    /// Validates document classes for a workflow execution.
    /// </summary>
    /// <param name="id">The workflow ID.</param>
    /// <param name="documentIds">Comma-separated list of document IDs to validate.</param>
    /// <returns>Validation result with errors, warnings, and status for each document class.</returns>
    /// <response code="200">Validation result returned successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="404">Workflow or documents not found.</response>
    /// <response code="500">Unexpected server error occurred.</response>
    [HttpGet("{id}/validate-documents")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DocumentClassValidationResultDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ValidateDocumentClasses(
        string id,
        [FromQuery] string documentIds)
    {
        if (string.IsNullOrWhiteSpace(documentIds))
        {
            return Problem("Document IDs parameter is required", statusCode: (int)HttpStatusCode.BadRequest);
        }

        List<string> documentIdList = [.. documentIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())];

        if (documentIdList.Count == 0)
        {
            return Problem("At least one document ID must be provided", statusCode: (int)HttpStatusCode.BadRequest);
        }

        var query = new ValidateDocumentClassesQuery(
            OrganizationId,
            UserId,
            id,
            documentIdList);

        ErrorOr<DocumentClassValidationResultDto> result = await _sender.Send(query);

        _logger.LogInformation("ValidateDocumentClasses for WorkflowId: {WorkflowId} - DocumentIds: {DocumentIds} - Result: {Result}",
            LogSanitizer.ForLog(id),
            LogSanitizer.ForLog(documentIds),
            LogSanitizer.ForLog(result.IsError ? $"Error - {result.FirstError.Description}" : "Success"));

        return result.Match<IActionResult>(
            Ok,
            Problem);
    }
}
