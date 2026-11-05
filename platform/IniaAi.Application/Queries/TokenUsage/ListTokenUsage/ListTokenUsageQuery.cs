using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.TokenUsage.ListTokenUsage;

/// <summary>
/// Represents a query to retrieve a paginated and OData-compliant list of token usage records.
/// </summary>
public record ListTokenUsageQuery : IRequest<ErrorOr<ODataPaginatedResponse<TokenUsageDto>>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListTokenUsageQuery"/> class.
    /// </summary>
    /// <param name="organizationId">The organization ID to filter token usage by.</param>
    /// <param name="userId">The user ID for authorization.</param>
    /// <param name="queryOptions">The OData query options to apply for filtering, sorting, and paging.</param>
    /// <param name="workflowTypeId">Optional workflow type ID to filter by.</param>
    public ListTokenUsageQuery(
        string organizationId,
        string userId,
        ODataQueryOptions<TokenUsageDto> queryOptions,
        string? workflowTypeId = null)
    {
        OrganizationId = organizationId;
        UserId = userId;
        QueryOptions = queryOptions;
        WorkflowTypeId = workflowTypeId;
    }

    /// <summary>
    /// Gets the organization ID to filter token usage by.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the user ID for authorization.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets or sets the OData query options used to retrieve token usage records.
    /// </summary>
    public ODataQueryOptions<TokenUsageDto> QueryOptions { get; set; }

    /// <summary>
    /// Gets the optional workflow type ID to filter by.
    /// </summary>
    public string? WorkflowTypeId { get; }
}
