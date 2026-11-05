using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.Counterparties.ListCounterpartyTypes;

/// <summary>
/// Represents a query to retrieve a paginated OData-compliant list of counterparty types.
/// </summary>
public record ListCounterpartyTypesQuery : IRequest<ErrorOr<ODataPaginatedResponse<CounterpartyTypeListItemDto>>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListCounterpartyTypesQuery"/> class.
    /// </summary>
    /// <param name="organizationId">The organization ID of the requesting user. Each type counts only this organization's counterparties.</param>
    /// <param name="userId">The user ID making the request.</param>
    /// <param name="queryOptions">The OData query options to apply for filtering, sorting, and paging.</param>
    public ListCounterpartyTypesQuery(string organizationId, string userId, ODataQueryOptions<CounterpartyTypeListItemDto> queryOptions)
    {
        OrganizationId = organizationId;
        UserId = userId;
        QueryOptions = queryOptions;
    }

    /// <summary>
    /// Gets the organization ID of the requesting user. Each type counts only this organization's counterparties.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the user ID making the request.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets or sets the OData query options for the counterparty types query.
    /// </summary>
    public ODataQueryOptions<CounterpartyTypeListItemDto> QueryOptions { get; set; }
}
