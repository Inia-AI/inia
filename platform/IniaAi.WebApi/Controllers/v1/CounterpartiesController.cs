using ErrorOr;
using IniaAi.Application.Commands.Counterparties.CreateCounterparty;
using IniaAi.Application.Commands.Counterparties.DeleteCounterparty;
using IniaAi.Application.Commands.Counterparties.ImportCounterparties;
using IniaAi.Application.Commands.Counterparties.UpdateCounterparty;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Counterparties.ExportCounterparties;
using IniaAi.Application.Queries.Counterparties.GetCounterparty;
using IniaAi.Application.Queries.Counterparties.ListCounterparties;
using IniaAi.Application.Queries.Counterparties.ListPeerCandidates;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Contracts.Requests.Counterparties;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders.Counterparty;
using IniaAi.WebApi.Helpers;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// API controller for operations related to counterparties.
/// Provides endpoints to retrieve summaries and filtered views of counterparty data.
/// </summary>
[Route("api/v{version:apiVersion}/counterparties")]
public class CounterpartiesController : ApiController
{
    private readonly ILogger _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="CounterpartiesController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sender">Sends each request to its handler.</param>
    public CounterpartiesController(
        IRequestContext requestContext,
        ILogger<CounterpartiesController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Retrieves all available Counterparties (filtered)
    /// </summary>
    /// <remarks>
    /// Supports OData filtering, ordering, and pagination.
    /// <br/><br/>
    /// You can apply OData query parameters like:
    /// <ul>
    /// <li><c>$filter</c> — filter by properties (e.g. <c>type eq 'workflow'</c>)</li>
    /// <li><c>$orderby</c> — sort results (e.g. <c>createdAt desc</c>)</li>
    /// <li><c>$top</c> — limit number of results</li>
    /// <li><c>$count=true</c> — include total count</li>
    /// </ul>
    /// </remarks>
    /// <param name="queryOptions">The OData query options (filter, order, skip, top, etc.).</param>
    /// <param name="counterpartyTypeId">Optional single counterparty type ID filter.</param>
    /// <param name="counterpartyTypeIds">Optional comma-separated counterparty type IDs filter.</param>
    /// <returns>A paged and filterable list of workflow outputs with OData metadata.</returns>
    /// <response code="200">Workflow outputs retrieved successfully.</response>
    /// <response code="400">User organization is missing or invalid.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpGet]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<CounterpartyListItemDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(CounterpartyPageResultResponseExample))]
    public async Task<ActionResult<ODataPaginatedResponse<CounterpartyListItemDto>>> GetCounterparties(
        ODataQueryOptions<CounterpartyListItemDto> queryOptions,
        [FromQuery] string? counterpartyTypeId = null,
        [FromQuery] string? counterpartyTypeIds = null)
    {
        if (string.IsNullOrEmpty(OrganizationId))
        {
            return BadRequest("User organization context is missing");
        }

        _logger.LogInformation("Retrieving Counterparties for OrganizationId: {OrganizationId}", OrganizationId);

        // Collect type IDs from both parameters
        var typeIds = new List<string>();

        if (!string.IsNullOrWhiteSpace(counterpartyTypeId))
        {
            typeIds.Add(counterpartyTypeId);
        }

        if (!string.IsNullOrWhiteSpace(counterpartyTypeIds))
        {
            typeIds.AddRange(counterpartyTypeIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var query = new ListCounterpartiesQuery(OrganizationId, UserId, queryOptions, typeIds.Count > 0 ? typeIds : null);
        ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>> result = await _sender.Send(query);

        return result.Match<ActionResult<ODataPaginatedResponse<CounterpartyListItemDto>>>(
        pageResult =>
        {
            return pageResult;
        },
            error => Problem()
        );
    }

    /// <summary>
    /// Retrieves the counterparties that may be offered as peer companies, each marked with
    /// whether its stored financial analysis can actually contribute a peer column.
    /// </summary>
    /// <remarks>
    /// When a workflow type is given, only counterparties of a type that workflow accepts (its
    /// counterparty type mappings — the same rule the counterparty picker filters by) are
    /// returned: a wrong-type counterparty is not a peer at all, so it is omitted rather than
    /// listed as unusable. Within the accepted types every counterparty is returned, including
    /// those that cannot be used — an entry marked <c>NotAnalysed</c> tells the user to run
    /// financial analysis for it, where omitting it would leave the absence unexplained.
    /// Eligibility is judged the way the given workflow's Peer Financials block reads stored
    /// data, so a counterparty reported as usable is one that workflow can render. A workflow
    /// type that maps no counterparty types offers every type, as the counterparty picker does.
    /// </remarks>
    /// <param name="workflowTypeId">Optional workflow type the peers are being chosen for.</param>
    /// <param name="search">Optional name fragment to narrow the list.</param>
    /// <param name="skip">How many candidates to skip, for paging.</param>
    /// <param name="top">How many candidates to return. Each costs a parse of its stored analysis.</param>
    /// <returns>The peer candidates with their availability.</returns>
    /// <response code="200">Peer candidates retrieved successfully.</response>
    /// <response code="400">User organization is missing or invalid.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="404">The given workflow type was not found or is not accessible.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("peer-candidates")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<PeerCandidateDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPeerCandidates(
        [FromQuery] string? workflowTypeId = null,
        [FromQuery] string? search = null,
        [FromQuery] int skip = 0,
        [FromQuery] int top = ListPeerCandidatesQuery.DEFAULT_TOP)
    {
        var query = new ListPeerCandidatesQuery(OrganizationId, UserId, workflowTypeId, search, skip, top);

        return (await _sender.Send(query)).Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Retrieves a single counterparty by ID.
    /// </summary>
    /// <param name="id">The unique identifier of the counterparty.</param>
    /// <returns>The counterparty with the specified ID.</returns>
    /// <response code="200">Counterparty retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Forbidden request.</response>
    /// <response code="404">Counterparty not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CounterpartyDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(CounterpartyPageResultResponseExample))]
    public async Task<IActionResult> GetById(string id)
    {
        if (string.IsNullOrEmpty(OrganizationId))
        {
            return BadRequest("User organization context is missing");
        }

        ErrorOr<CounterpartyDto> result = await _sender.Send(new GetCounterpartyQuery(id, OrganizationId));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Creates a new counterparty.
    /// </summary>
    /// <param name="request">The counterparty creation request payload.</param>
    /// <returns>The newly created counterparty.</returns>
    /// <response code="201">Counterparty created successfully.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Forbidden request.</response>
    /// <response code="404">Counterparty not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(CounterpartyDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerRequestExample(typeof(CreateCounterpartyRequest), typeof(CreateCounterpartyRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(CounterpartyDtoExample))]
    public async Task<IActionResult> Create([FromBody] CreateCounterpartyRequest request)
    {
        if (string.IsNullOrEmpty(OrganizationId))
        {
            return BadRequest("User organization context is missing");
        }

        var command = new CreateCounterpartyCommand(
             request.Id,
             OrganizationId,
             request.ParentId,
             request.CounterpartyTypeIds,
             request.Name,
             request.NameVariants,
             request.Email,
             request.PhoneNumber,
             request.Website,
             request.Address,
             request.LastFinancialAnalysisResult,
             request.Properties
         );

        ErrorOr<CounterpartyDto> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            counterparty => CreatedAtAction(nameof(GetById), new { id = counterparty.Id }, counterparty),
            Problem
        );
    }

    /// <summary>
    /// Updates an existing counterparty.
    /// </summary>
    /// <param name="id">The ID of the counterparty to update.</param>
    /// <param name="request">The updated counterparty data.</param>
    /// <returns>A result indicating success or failure.</returns>
    /// <response code="200">Counterparty updated successfully.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Forbidden request.</response>
    /// <response code="404">Counterparty not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(UpdateCounterpartyRequestExample))]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateCounterpartyRequest request)
    {
        if (string.IsNullOrEmpty(OrganizationId))
        {
            return BadRequest("User organization context is missing");
        }

        var command = new UpdateCounterpartyCommand(
              id,
             OrganizationId,
              request.ParentId,
              request.CounterpartyTypeIds,
              request.Name,
              request.NameVariants,
              request.Email,
              request.PhoneNumber,
              request.Website,
              request.Address,
              request.LastFinancialAnalysisResult,
              request.Properties
          );

        ErrorOr<Updated> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => Ok(),
            Problem
        );
    }

    /// <summary>
    /// Deletes a counterparty.
    /// </summary>
    /// <param name="id">The ID of the counterparty to delete.</param>
    /// <returns>No content if successful, or an error response.</returns>
    /// <response code="204">Counterparty deleted successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">Forbidden request.</response>
    /// <response code="404">Workflow output not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(CounterpartyPageResultResponseExample))]
    public async Task<IActionResult> Delete(string id)
    {
        if (string.IsNullOrEmpty(OrganizationId))
        {
            return BadRequest("User organization context is missing");
        }

        var command = new DeleteCounterpartyCommand(id, OrganizationId);

        ErrorOr<Deleted> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            Problem
        );
    }

    /// <summary>
    /// Exports every active counterparty of the organization as one JSON bundle: the hierarchy,
    /// the types by name, the property bags and the stored financial analysis, with the ids kept
    /// so an import on another instance lands the same ids. Document links and user links do not
    /// travel.
    /// </summary>
    /// <response code="200">The bundle as a JSON file download.</response>
    /// <response code="403">The caller is not an administrator.</response>
    [HttpGet("export")]
    [Authorize(Roles = Roles.ADMIN_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export()
    {
        ErrorOr<CounterpartyBundleDto> result = await _sender.Send(new ExportCounterpartiesQuery(OrganizationId, UserId, InstanceName));
        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        await CounterpartyBundleJson.WriteDownloadAsync(Response, result.Value, OrganizationId, HttpContext.RequestAborted);

        return new EmptyResult();
    }

    /// <summary>
    /// Imports a counterparty bundle produced by the export endpoint into the organization. Each
    /// row matches an existing counterparty by id, then by name, else is created with its source
    /// id; bundle values win on an update; nothing is ever deleted. A row that cannot land is
    /// reported with its reason and the rest lands in one commit. With <c>dryRun=true</c> the
    /// same plan is returned and nothing is written.
    /// </summary>
    /// <param name="bundle">The JSON file from the export endpoint.</param>
    /// <param name="dryRun">When true, compute the plan and write nothing.</param>
    /// <response code="200">The plan: one row per bundled counterparty with its disposition, and the counts.</response>
    /// <response code="400">The bundle is malformed, of an unsupported version, or its parent links form a cycle.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="409">A concurrent change to the organization's counterparties collided with the import; nothing was written.</response>
    [HttpPost("import")]
    [Authorize(Roles = Roles.ADMIN_ROLES)]
    [RequestSizeLimit(LARGE_PAYLOAD_LIMIT_BYTES)]
    [ProducesResponseType(typeof(CounterpartyImportPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(List<Error>))]
    public async Task<IActionResult> Import([FromBody] CounterpartyBundleDto bundle, [FromQuery] bool dryRun = false)
    {
        ErrorOr<CounterpartyImportPlanDto> result = await _sender.Send(new ImportCounterpartiesCommand(OrganizationId, UserId, bundle, dryRun));

        return result.Match(Ok, Problem);
    }
}
