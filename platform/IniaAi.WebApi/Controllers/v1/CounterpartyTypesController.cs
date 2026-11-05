using ErrorOr;
using IniaAi.Application.Commands.CounterpartyTypes.CreateCounterpartyType;
using IniaAi.Application.Commands.CounterpartyTypes.DeleteCounterpartyType;
using IniaAi.Application.Commands.CounterpartyTypes.UpdateCounterpartyType;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Counterparties.ListCounterpartyTypes;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests.CounterpartyTypes;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders.Counterparty;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// API controller for operations related to counterparty types.
/// Provides endpoints to retrieve summaries and filtered views of counterparty types data.
/// </summary>
[Route("api/v{version:apiVersion}/counterparty-types")]
public class CounterpartyTypesController : ApiController
{
    private readonly ILogger _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="CounterpartyTypesController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sender">Sends each request to its handler.</param>
    public CounterpartyTypesController(
        IRequestContext requestContext,
        ILogger<CounterpartyTypesController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Retrieves all available Counterparty Types (filtered)
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
    /// <returns>A paged and filterable list of workflow outputs with OData metadata.</returns>
    /// <response code="200">Workflow outputs retrieved successfully.</response>
    /// <response code="400">User organization is missing or invalid.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpGet]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<CounterpartyTypeListItemDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(CounterpartyPageResultResponseExample))]
    public async Task<ActionResult<ODataPaginatedResponse<CounterpartyTypeListItemDto>>> GetCounterparties(ODataQueryOptions<CounterpartyTypeListItemDto> queryOptions)
    {
        _logger.LogInformation("Retrieving CounterpartyTypes");

        var query = new ListCounterpartyTypesQuery(OrganizationId, UserId, queryOptions);
        ErrorOr<ODataPaginatedResponse<CounterpartyTypeListItemDto>> result = await _sender.Send(query);

        return result.Match<ActionResult<ODataPaginatedResponse<CounterpartyTypeListItemDto>>>(
        pageResult =>
        {
            return pageResult;
        },
            error => Problem()
        );
    }

    /// <summary>
    /// Creates a counterparty type. Counterparty types are shared by every organization, so only an
    /// editor creates one.
    /// </summary>
    /// <param name="request">The name, unique ignoring case, and the description.</param>
    /// <returns>The new counterparty type.</returns>
    /// <response code="201">Counterparty type created.</response>
    /// <response code="400">The name is empty or too long.</response>
    /// <response code="403">The caller is not an editor.</response>
    /// <response code="409">Another counterparty type has the name.</response>
    [HttpPost]
    [Authorize(Roles = Roles.EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(CounterpartyTypeListItemDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(List<Error>))]
    public async Task<IActionResult> CreateCounterpartyType([FromBody] CounterpartyTypeRequest request)
    {
        ErrorOr<CounterpartyTypeListItemDto> result = await _sender.Send(
            new CreateCounterpartyTypeCommand(OrganizationId, UserId, request.Name, request.Description));

        return result.Match<IActionResult>(
            created => CreatedAtAction(nameof(GetCounterparties), new { id = created.Id }, created),
            Problem);
    }

    /// <summary>
    /// Replaces the name and description of a counterparty type. A built-in type keeps its name,
    /// because the platform finds it by name; its description can change.
    /// </summary>
    /// <param name="id">The counterparty type to change.</param>
    /// <param name="request">The new name and description.</param>
    /// <returns>The changed counterparty type.</returns>
    /// <response code="200">Counterparty type changed.</response>
    /// <response code="400">The name is empty or too long.</response>
    /// <response code="403">The caller is not an editor.</response>
    /// <response code="404">Counterparty type not found.</response>
    /// <response code="409">Another counterparty type has the name, or the type is built in and the name changed.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = Roles.EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CounterpartyTypeListItemDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(List<Error>))]
    public async Task<IActionResult> UpdateCounterpartyType(string id, [FromBody] CounterpartyTypeRequest request)
    {
        ErrorOr<CounterpartyTypeListItemDto> result = await _sender.Send(
            new UpdateCounterpartyTypeCommand(OrganizationId, UserId, id, request.Name, request.Description));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Deletes a counterparty type. Its counterparties keep their other types. A built-in type cannot
    /// be deleted.
    /// </summary>
    /// <param name="id">The counterparty type to delete.</param>
    /// <response code="204">Counterparty type deleted.</response>
    /// <response code="403">The caller is not an editor.</response>
    /// <response code="404">Counterparty type not found.</response>
    /// <response code="409">The type is built in.</response>
    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.EDITOR_ROLES)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(List<Error>))]
    public async Task<IActionResult> DeleteCounterpartyType(string id)
    {
        ErrorOr<Deleted> result = await _sender.Send(new DeleteCounterpartyTypeCommand(OrganizationId, UserId, id));

        return result.Match<IActionResult>(_ => NoContent(), Problem);
    }
}
