using ErrorOr;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Organizations.GetUserTenants;
using IniaAi.Application.Queries.Users.ResolveUsers;
using IniaAi.Application.Queries.WorkflowOutputs.PaginatedResponseWorkflowOutputs;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests.Users;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Organizations;
using IniaAi.WebApi.Contracts.Shared.Users;
using IniaAi.WebApi.Controllers.v1.Base;
using IniaAi.WebApi.ExampleProviders.WorkflowOutput;
using IniaAi.WebApi.Swagger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// Provides operations for managing workflow output elements, and also handles export operations
/// </summary>
[Route("api/v{version:apiVersion}/users")]
public class UsersController : ApiController
{
    private readonly ILogger<UsersController> _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsersController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">Logger for recording operational events.</param>
    /// <param name="sender">Sends each request to its handler.</param>
    public UsersController(
        IRequestContext requestContext,
        ILogger<UsersController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Retrieves all available users for the current user's tenant. (filtered)
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
    /// <response code="400">User tenant is missing or invalid.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpGet]
    [RequiresCapability(OrganizationCapabilities.TEAM_VIEW)]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<UserDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ListUsersResponseExample))]
    public async Task<ActionResult<ODataPaginatedResponse<UserDto>>> GetUsers(ODataQueryOptions<UserDto> queryOptions)
    {
        var query = new ListUsersQuery(OrganizationId, UserId, queryOptions);
        ErrorOr<ODataPaginatedResponse<UserDto>> result = await _sender.Send(query);

        _logger.LogInformation(
            "Get users request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, Query: {Query}, Success: {Success}",
            OrganizationId,
            UserId,
            queryOptions.RawValues,
            result.IsError ? "No" : "Yes");

        return result.Match<ActionResult<ODataPaginatedResponse<UserDto>>>(
            pageResult => { return pageResult; },
            error => Problem());
    }

    /// <summary>
    /// Retrieves the tenants that the current user belongs to.
    /// </summary>
    /// <remarks>
    /// Returns a list of all tenants the authenticated user has access to.
    /// This endpoint does not require the X-Tenant-Id header as it retrieves available tenants.
    /// The list is ordered alphabetically by tenant name.
    /// </remarks>
    /// <returns>A list of tenants the user belongs to.</returns>
    /// <response code="200">Tenants retrieved successfully.</response>
    /// <response code="204">No tenants found for the user.</response>
    /// <response code="401">User is not authenticated.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpGet("me/tenants")]
    [TenantOptional]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ListResponse<UserTenantDto>))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMyTenants()
    {
        if (string.IsNullOrEmpty(UserId))
        {
            return Unauthorized(Error.Unauthorized(description: "User not authenticated"));
        }

        ErrorOr<List<UserTenantDto>> result = await _sender.Send(new GetUserTenantsQuery(UserId));

        _logger.LogInformation(
            "Get user tenants request processed for UserId: {UserId}, Success: {Success}",
            UserId,
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(
            tenants => tenants.Count > 0
                ? Ok(new ListResponse<UserTenantDto>(tenants))
                : NoContent(),
            Problem);
    }

    /// <summary>
    /// Resolves usernames to their display information (full name).
    /// </summary>
    /// <remarks>
    /// Only resolves users who have created workflow reports in the current tenant.
    /// This is a security measure to prevent arbitrary user lookups.
    /// If a username cannot be resolved, it will not be included in the response.
    /// </remarks>
    /// <param name="request">The request containing the usernames to resolve.</param>
    /// <returns>A list of resolved user information.</returns>
    /// <response code="200">Users resolved successfully.</response>
    /// <response code="400">Invalid request format.</response>
    /// <response code="401">User is not authenticated.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpPost("resolve")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ListResponse<ResolvedUserDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(List<Error>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResolveUsers([FromBody] ResolveUsersRequest request)
    {
        var query = new ResolveUsersQuery(OrganizationId, UserId, request.Usernames);
        ErrorOr<List<ResolvedUserDto>> result = await _sender.Send(query);

        _logger.LogInformation(
            "Resolve users request processed for OrganizationId: {OrganizationId}, UserId: {UserId}, Success: {Success}",
            OrganizationId,
            UserId,
            result.IsError ? "No" : "Yes");

        return result.Match<IActionResult>(
            users => Ok(new ListResponse<ResolvedUserDto>(users)),
            Problem);
    }
}
