using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Jobs;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.Jobs.ListJobs;

/// <summary>
/// Represents a query to retrieve a paginated and OData-compliant list of jobs.
/// </summary>
public record PaginatedResponseJobsQuery : IRequest<ErrorOr<ODataPaginatedResponse<JobDto>>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PaginatedResponseJobsQuery"/> class.
    /// </summary>
    /// <param name="organizationId">The organization ID to filter jobs by.</param>
    /// <param name="userId">The user ID to filter jobs by.</param>
    /// <param name="queryOptions">The OData query options to apply for filtering, sorting, and paging jobs.</param>
    public PaginatedResponseJobsQuery(string organizationId, string userId, ODataQueryOptions<JobDto> queryOptions)
    {
        OrganizationId = organizationId;
        UserId = userId;
        QueryOptions = queryOptions;
    }

    /// <summary>
    /// Gets the organization ID to filter jobs by.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the user ID to filter jobs by.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets or sets the OData query options used to retrieve jobs.
    /// </summary>
    public ODataQueryOptions<JobDto> QueryOptions { get; set; }
}
