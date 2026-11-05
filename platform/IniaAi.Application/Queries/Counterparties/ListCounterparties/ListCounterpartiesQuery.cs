using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.Counterparties.ListCounterparties;

/// <summary>
/// Represents a query to retrieve a paginated OData-compliant list of counterparties.
/// </summary>
public record ListCounterpartiesQuery : IRequest<ErrorOr<ODataPaginatedResponse<CounterpartyListItemDto>>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListCounterpartiesQuery"/> class.
    /// </summary>
    /// <param name="organizationId">The organization ID to filter counterparties by.</param>
    /// <param name="userId">The user ID making the request.</param>
    /// <param name="queryOptions">The OData query options to apply for filtering, sorting, and paging.</param>
    /// <param name="counterpartyTypeIds">Optional counterparty type IDs to filter by.</param>
    public ListCounterpartiesQuery(string organizationId, string userId, ODataQueryOptions<CounterpartyListItemDto> queryOptions, List<string>? counterpartyTypeIds = null)
    {
        OrganizationId = organizationId;
        UserId = userId;
        QueryOptions = queryOptions;
        CounterpartyTypeIds = counterpartyTypeIds;
    }

    /// <summary>
    /// Gets the organization ID to filter counterparties by.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the user ID making the request.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets or sets the OData query options for the counterparties query.
    /// </summary>
    public ODataQueryOptions<CounterpartyListItemDto> QueryOptions { get; set; }

    /// <summary>
    /// Gets the optional counterparty type IDs to filter by.
    /// </summary>
    public List<string>? CounterpartyTypeIds { get; }
}
