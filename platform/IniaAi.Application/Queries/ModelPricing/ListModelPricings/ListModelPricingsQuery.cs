using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.ModelPricing;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.ModelPricing.ListModelPricings;

/// <summary>
/// Represents a query to retrieve a paginated OData-compliant list of model pricings.
/// </summary>
public record ListModelPricingsQuery : IRequest<ErrorOr<ODataPaginatedResponse<ModelPricingListItemDto>>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListModelPricingsQuery"/> class.
    /// </summary>
    /// <param name="queryOptions">The OData query options to apply for filtering, sorting, and paging.</param>
    public ListModelPricingsQuery(ODataQueryOptions<ModelPricingListItemDto> queryOptions)
    {
        QueryOptions = queryOptions;
    }

    /// <summary>
    /// Gets or sets the OData query options for the model pricings query.
    /// </summary>
    public ODataQueryOptions<ModelPricingListItemDto> QueryOptions { get; set; }
}
