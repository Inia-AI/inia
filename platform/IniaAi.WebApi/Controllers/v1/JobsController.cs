using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Jobs.GetJob;
using IniaAi.Application.Queries.Jobs.ListJobs;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Jobs;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders.Jobs;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Handles job-related endpoints, including job listing and retrieval.
/// </summary>
[Route("api/v{version:apiVersion}/jobs")]
public class JobsController : ApiController
{
    private readonly ILogger<JobsController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobsController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="sender">Sends each application query to its handler.</param>
    public JobsController(
        IRequestContext requestContext,
        ILogger<JobsController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Retrieves a job by its unique identifier.
    /// </summary>
    /// <param name="id">The job ID to retrieve.</param>
    /// <returns>The job details.</returns>
    /// <response code="200">Job retrieved successfully.</response>
    /// <response code="404">Job not found, or it belongs to another organization.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="403">The job belongs to another user.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(JobDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(BackgroundJobResponseExample))]
    public async Task<IActionResult> GetJobById(string id)
    {
        ErrorOr<JobDto> result = await _sender.Send(new GetJobQuery(id, OrganizationId, UserId));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves a paginated, filterable list of jobs using OData query options.
    /// </summary>
    /// <remarks>
    /// Supports OData query capabilities such as:
    /// <list type="bullet">
    /// <item><description><c>$filter</c> � filter by job properties</description></item>
    /// <item><description><c>$orderby</c> � sort by fields like <c>createdAt</c></description></item>
    /// <item><description><c>$top</c>, <c>$skip</c> � pagination</description></item>
    /// <item><description><c>$count=true</c> � include total record count</description></item>
    /// </list>
    /// </remarks>
    /// <param name="queryOptions">OData query options for filtering, sorting, and paging.</param>
    /// <returns>An OData-compliant paginated list of jobs.</returns>
    /// <response code="200">Jobs retrieved successfully.</response>
    /// <response code="400">Invalid request or missing organization context.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<JobDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(JobPageResultResponseExample))]

    public async Task<ActionResult<ODataPaginatedResponse<JobDto>>> GetWorkflowOutputs(ODataQueryOptions<JobDto> queryOptions)
    {
        _logger.LogInformation("Retrieving jobs for OrganizationId: {OrganizationId} and UserId: {UserId}", OrganizationId, UserId);

        var query = new PaginatedResponseJobsQuery(OrganizationId, UserId, queryOptions);
        ErrorOr<ODataPaginatedResponse<JobDto>> result = await _sender.Send(query);

        return result.Match<ActionResult<ODataPaginatedResponse<JobDto>>>(
            pageResult => pageResult,
            error => Problem());
    }
}
