using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Shared.Users;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.WorkflowOutputs.PaginatedResponseWorkflowOutputs;

/// <summary>
/// Represents a query to retrieve a paginated OData-compliant list of users.
/// </summary>
public record ListUsersQuery : IRequest<ErrorOr<ODataPaginatedResponse<UserDto>>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ListUsersQuery"/> class.
    /// </summary>
    /// <param name="organizationId">The organization ID to filter users by.</param>
    /// <param name="userId">The user ID making the request.</param>
    /// <param name="queryOptions">The OData query options to apply for filtering, sorting, and paging.</param>
    public ListUsersQuery(string organizationId, string userId, ODataQueryOptions<UserDto> queryOptions)
    {
        OrganizationId = organizationId;
        UserId = userId;
        QueryOptions = queryOptions;
    }

    /// <summary>
    /// Gets the organization ID to filter users by.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the user ID making the request.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets or sets the OData query options for the users query.
    /// </summary>
    public ODataQueryOptions<UserDto> QueryOptions { get; set; }
}
