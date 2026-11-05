using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.TenantModelSettings;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.OrganizationModelSettings.ListOrganizationModelSettings;

/// <summary>
/// Represents a query to retrieve a paginated OData-compliant list of organization model settings.
/// </summary>
public record ListOrganizationModelSettingsQuery : IRequest<ErrorOr<ODataPaginatedResponse<TenantModelSettingsListItemDto>>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListOrganizationModelSettingsQuery"/> class.
    /// </summary>
    /// <param name="queryOptions">The OData query options to apply for filtering, sorting, and paging.</param>
    public ListOrganizationModelSettingsQuery(ODataQueryOptions<TenantModelSettingsListItemDto> queryOptions)
    {
        QueryOptions = queryOptions;
    }

    /// <summary>
    /// Gets or sets the OData query options for the organization model settings query.
    /// </summary>
    public ODataQueryOptions<TenantModelSettingsListItemDto> QueryOptions { get; set; }
}
