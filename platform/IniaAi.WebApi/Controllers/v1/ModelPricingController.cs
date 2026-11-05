using ErrorOr;
using IniaAi.Application.Commands.ModelPricing.CreateModelPricing;
using IniaAi.Application.Commands.ModelPricing.DeleteModelPricing;
using IniaAi.Application.Commands.ModelPricing.UpdateModelPricing;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.ModelPricing.GetModelPricing;
using IniaAi.Application.Queries.ModelPricing.ListModelPricings;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Attributes;
using IniaAi.WebApi.Contracts.Requests.ModelPricing;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;
using IniaAi.WebApi.Controllers.v1.Base;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using DomainComponentType = IniaAi.Domain.Entities.TokenUsage.Enums.PricingComponentType;
using DomainPricingType = IniaAi.Domain.Entities.TokenUsage.Enums.PricingType;
using DomainPricingUnit = IniaAi.Domain.Entities.TokenUsage.Enums.PricingUnit;

namespace IniaAi.WebApi.Controllers.v1;

/// <summary>
/// API controller for managing model pricing entries.
/// Provides CRUD operations for AI model pricing configuration.
/// Operator and SuperAdmin access only.
/// </summary>
[Route("api/v{version:apiVersion}/model-pricing")]
[Authorize(Roles = Roles.OPERATOR_ROLES)]
public class ModelPricingController : ApiController
{
    private readonly ILogger _logger;
    private readonly ISender _sender;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelPricingController"/> class.
    /// </summary>
    /// <param name="requestContext">The request context containing organization and user information.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sender">Sends each request to its handler.</param>
    public ModelPricingController(
        IRequestContext requestContext,
        ILogger<ModelPricingController> logger,
        ISender sender)
        : base(requestContext)
    {
        _logger = logger;
        _sender = sender;
    }

    /// <summary>
    /// Retrieves all model pricing entries with OData support.
    /// </summary>
    /// <remarks>
    /// Supports OData filtering, ordering, and pagination.
    /// <br/><br/>
    /// You can apply OData query parameters like:
    /// <ul>
    /// <li><c>$filter</c> — filter by properties (e.g. <c>provider eq 'anthropic'</c>)</li>
    /// <li><c>$orderby</c> — sort results (e.g. <c>displayName asc</c>)</li>
    /// <li><c>$top</c> — limit number of results</li>
    /// <li><c>$count=true</c> — include total count</li>
    /// </ul>
    /// </remarks>
    /// <param name="queryOptions">The OData query options (filter, order, skip, top, etc.).</param>
    /// <returns>A paged and filterable list of model pricings with OData metadata.</returns>
    /// <response code="200">Model pricings retrieved successfully.</response>
    /// <response code="401">User is unauthorized.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="500">Internal server error occurred.</response>
    [HttpGet]
    [EnableCustomQuery]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ODataPaginatedResponse<ModelPricingListItemDto>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ODataPaginatedResponse<ModelPricingListItemDto>>> GetModelPricings(
        ODataQueryOptions<ModelPricingListItemDto> queryOptions)
    {
        _logger.LogInformation("Retrieving model pricings");

        var query = new ListModelPricingsQuery(queryOptions);
        ErrorOr<ODataPaginatedResponse<ModelPricingListItemDto>> result = await _sender.Send(query);

        return result.Match<ActionResult<ODataPaginatedResponse<ModelPricingListItemDto>>>(
            pageResult => pageResult,
            error => Problem([.. error])
        );
    }

    /// <summary>
    /// Retrieves a single model pricing entry by ID.
    /// </summary>
    /// <param name="id">The unique identifier of the model pricing.</param>
    /// <returns>The model pricing with the specified ID.</returns>
    /// <response code="200">Model pricing retrieved successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="404">Model pricing not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ModelPricingDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(string id)
    {
        ErrorOr<ModelPricingDto> result = await _sender.Send(new GetModelPricingQuery(id));

        return result.Match<IActionResult>(Ok, Problem);
    }

    /// <summary>
    /// Creates a new model pricing entry.
    /// </summary>
    /// <param name="request">The model pricing creation request payload.</param>
    /// <returns>The newly created model pricing.</returns>
    /// <response code="201">Model pricing created successfully.</response>
    /// <response code="400">Invalid request format or validation error.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="409">Duplicate provider/model combination.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ModelPricingDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create([FromBody] CreateModelPricingRequest request)
    {
        // Convert contract enum to domain enum
        var domainPricingType = (DomainPricingType)request.PricingType;

        // Convert contract components to domain components
        var components = request.Components.Select(c => new PricingComponentData(
            (DomainComponentType)c.ComponentType,
            c.Cost,
            (DomainPricingUnit)c.Unit,
            c.Currency
        )).ToList();

        var command = new CreateModelPricingCommand(
            request.Provider,
            request.ModelId,
            request.ModelAlias,
            request.DisplayName,
            domainPricingType,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.IsActive,
            components,
            request.Family,
            request.LongPromptThresholdTokens
        );

        ErrorOr<ModelPricingDto> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            modelPricing => CreatedAtAction(nameof(GetById), new { id = modelPricing.Id }, modelPricing),
            Problem
        );
    }

    /// <summary>
    /// Updates an existing model pricing entry.
    /// </summary>
    /// <param name="id">The ID of the model pricing to update.</param>
    /// <param name="request">The updated model pricing data.</param>
    /// <returns>A result indicating success or failure.</returns>
    /// <response code="200">Model pricing updated successfully.</response>
    /// <response code="400">Invalid request format or validation error.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="404">Model pricing not found.</response>
    /// <response code="409">Duplicate provider/model combination.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateModelPricingRequest request)
    {
        // Convert contract enum to domain enum
        var domainPricingType = (DomainPricingType)request.PricingType;

        // Convert contract components to domain components
        var components = request.Components.Select(c => new PricingComponentData(
            (DomainComponentType)c.ComponentType,
            c.Cost,
            (DomainPricingUnit)c.Unit,
            c.Currency
        )).ToList();

        var command = new UpdateModelPricingCommand(
            id,
            request.Provider,
            request.ModelId,
            request.ModelAlias,
            request.DisplayName,
            domainPricingType,
            request.EffectiveFrom,
            request.EffectiveTo,
            request.IsActive,
            components,
            request.Family,
            request.LongPromptThresholdTokens
        );

        ErrorOr<Updated> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => Ok(),
            Problem
        );
    }

    /// <summary>
    /// Deletes a model pricing entry.
    /// </summary>
    /// <param name="id">The ID of the model pricing to delete.</param>
    /// <returns>No content if successful, or an error response.</returns>
    /// <response code="204">Model pricing deleted successfully.</response>
    /// <response code="401">Unauthorized access.</response>
    /// <response code="403">User holds neither the Operator nor the SuperAdmin role.</response>
    /// <response code="404">Model pricing not found.</response>
    /// <response code="500">Server error occurred while processing the request.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete(string id)
    {
        var command = new DeleteModelPricingCommand(id);

        ErrorOr<Deleted> result = await _sender.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            Problem
        );
    }
}
