using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.OData;
using IniaAi.Application.Queries.Users;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Specifications.Users;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Shared.Users;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.WorkflowOutputs.PaginatedResponseWorkflowOutputs;

/// <summary>
/// Handles the query to retrieve a paginated and OData-compliant list of <see cref="UserDto"/>.
/// </summary>
public class ListUsersQueryHandler
    : IRequestHandler<ListUsersQuery, ErrorOr<ODataPaginatedResponse<UserDto>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListUsersQueryHandler"/> class.
    /// </summary>
    /// <param name="currentUser">The current user service to get context information.</param>
    /// <param name="unitOfWork">The unit of work for user data repository access.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public ListUsersQueryHandler(
        IUserDataUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Handles the incoming request to return paginated workflow outputs.
    /// </summary>
    /// <param name="request">The request containing query options.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A paginated OData response containing workflow output DTOs.</returns>
    public async Task<ErrorOr<ODataPaginatedResponse<UserDto>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        // Create specification for the query
        var specification = new NonSystemUsersByOrganizationSpec(request.OrganizationId);

        // Get base query using repository with specification and add read-only optimization
        IQueryable<User> baseQuery = _unitOfWork.Users.GetAll(specification).AsNoTracking();

        return await baseQuery.Select(UserProjections.ToDto).ToODataPageAsync(request.QueryOptions, cancellationToken);
    }
}
