using ErrorOr;
using IniaAi.Application.Commands.OrganizationModelSettings.CreateOrganizationModelSettings;
using IniaAi.Application.Commands.OrganizationModelSettings.DeleteOrganizationModelSettings;
using IniaAi.Application.Commands.OrganizationModelSettings.UpdateOrganizationModelSettings;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.OrganizationModelSettings.GetOrganizationModelSettings;
using IniaAi.Application.Queries.OrganizationModelSettings.ListOrganizationModelSettings;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests.TenantModelSettings;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.TenantModelSettings;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using DomainUsageContext = IniaAi.Domain.Entities.TokenUsage.Enums.UsageContext;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// API controller for managing tenant model settings.
/// Provides CRUD operations for per-tenant AI model configuration.
/// Operator and SuperAdmin access only.
/// </summary>
[Route("api/v{version:apiVersion}/tenant-model-settings")]
[Authorize(Roles = Roles.OPERATOR_ROLES)]
public class TenantModelSettingsController : ApiController
{
    private readonly ILogger _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantModelSettingsController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sender">Sends each request to its handler.</param>
    public TenantModelSettingsController(
        IRequestContext requestContext,
        ILogger<TenantModelSettingsController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Retrieves all tenant model settings with OData support.
    /// </summary>
    /// <remarks>
    /// Supports OData filtering, ordering, and pagination.
    /// <br/><br/>
    /// You can apply OData query parameters like:
    /// <ul>
    /// <li><c>$filter</c> — filter by properties (e.g. <c>usageContext eq 'BlockExecution'</c>)</li>
    /// <li><c>$orderby</c> — sort results (e.g. <c>tenantDisplayName asc</c>)</li>
    /// <li><c>$top</c> — limit number of results</li>
    /// <li><c>$count=true</c> — include total count</li>
    /// </ul>
    /// </remarks>
    /// <param name="queryOptions">The OData query options (filter, order, skip, top, etc.).</param>
    /// <returns>A paged and filterable list of tenant model settings with OData metadata.</returns>
    /// <response code="200">Tenant model settings retrieved successfully.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpGet]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<TenantModelSettingsListItemDto>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ODataPaginatedResponse<TenantModelSettingsListItemDto>>> GetTenantModelSettings(
        ODataQueryOptions<TenantModelSettingsListItemDto> queryOptions)
    {
        _logger.LogInformation("Retrieving tenant model settings");

        var query = new ListOrganizationModelSettingsQuery(queryOptions);
        ErrorOr<ODataPaginatedResponse<TenantModelSettingsListItemDto>> result = await _sender.Send(query);

        return result.Match<ActionResult<ODataPaginatedResponse<TenantModelSettingsListItemDto>>>(
            pageResult => pageResult,
            error => Problem([.. error])
        );
    }

    /// <summary>
    /// Retrieves a single tenant model settings entry by ID.
    /// </summary>
    /// <param name="id">The unique identifier of the tenant model settings.</param>
    /// <returns>The tenant model settings with the specified ID.</returns>
    /// <response code="200">Tenant model settings retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="404">Tenant model settings not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TenantModelSettingsDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(string id)
    {
        ErrorOr<TenantModelSettingsDto> result = await _sender.Send(new GetOrganizationModelSettingsQuery(id));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Creates a new tenant model settings entry.
    /// </summary>
    /// <param name="request">The tenant model settings creation request payload.</param>
    /// <returns>The newly created tenant model settings.</returns>
    /// <response code="201">Tenant model settings created successfully.</response>
    /// <response code="400">Invalid request format or validation error.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="404">Referenced tenant or model pricing not found.</response>
    /// <response code="409">Duplicate tenant/usage context combination.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(TenantModelSettingsDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateTenantModelSettingsRequest request)
    {
        // Convert contract enum to domain enum
        var domainUsageContext = (DomainUsageContext)request.UsageContext;

        var command = new CreateOrganizationModelSettingsCommand(
            request.TenantId,
            domainUsageContext,
            request.ModelPricingId,
            request.IsEnabled,
            request.Priority
        );

        ErrorOr<TenantModelSettingsDto> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            settings => CreatedAtAction(nameof(GetById), new { id = settings.Id }, settings),
            Problem
        );
    }

    /// <summary>
    /// Updates an existing tenant model settings entry.
    /// </summary>
    /// <param name="id">The ID of the tenant model settings to update.</param>
    /// <param name="request">The updated tenant model settings data.</param>
    /// <returns>A result indicating success or failure.</returns>
    /// <response code="200">Tenant model settings updated successfully.</response>
    /// <response code="400">Invalid request format or validation error.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="404">Tenant model settings, tenant, or model pricing not found.</response>
    /// <response code="409">Duplicate tenant/usage context combination.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateTenantModelSettingsRequest request)
    {
        // Convert contract enum to domain enum
        var domainUsageContext = (DomainUsageContext)request.UsageContext;

        var command = new UpdateOrganizationModelSettingsCommand(
            id,
            request.TenantId,
            domainUsageContext,
            request.ModelPricingId,
            request.IsEnabled,
            request.Priority
        );

        ErrorOr<Updated> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => Ok(),
            Problem
        );
    }

    /// <summary>
    /// Deletes a tenant model settings entry.
    /// </summary>
    /// <param name="id">The ID of the tenant model settings to delete.</param>
    /// <returns>No content if successful, or an error response.</returns>
    /// <response code="204">Tenant model settings deleted successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="404">Tenant model settings not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(string id)
    {
        var command = new DeleteOrganizationModelSettingsCommand(id);

        ErrorOr<Deleted> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            Problem
        );
    }
}
