using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Shared.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.Users.ResolveUsers;

/// <summary>
/// Handles the query to resolve usernames to their display information (full name).
/// Only resolves users who have created workflow reports in the specified tenant.
/// </summary>
public class ResolveUsersQueryHandler : IRequestHandler<ResolveUsersQuery, ErrorOr<List<ResolvedUserDto>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<ResolveUsersQueryHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolveUsersQueryHandler"/> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work for data access.</param>
    /// <param name="logger">The logger instance.</param>
    public ResolveUsersQueryHandler(
        IUserDataUnitOfWork unitOfWork,
        ILogger<ResolveUsersQueryHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Handles the query request to resolve usernames.
    /// </summary>
    /// <param name="request">The query request containing the usernames to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of resolved user DTOs or an error.</returns>
    public async Task<ErrorOr<List<ResolvedUserDto>>> Handle(
        ResolveUsersQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UsernamesToResolve.Count == 0)
        {
            return new List<ResolvedUserDto>();
        }

        try
        {
            // Get usernames (AnalystName) that exist in workflow executions for this tenant
            List<string> allowedUsernames = await _unitOfWork.WorkflowExecutions
                .GetAll()
                .AsNoTracking()
                .Where(we => we.OrganizationId == request.OrganizationId
                    && we.AnalystName != null
                    && request.UsernamesToResolve.Contains(we.AnalystName))
                .Select(we => we.AnalystName!)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (allowedUsernames.Count == 0)
            {
                _logger.LogDebug(
                    "No matching usernames found in workflow executions for tenant {OrganizationId}",
                    request.OrganizationId);
                return new List<ResolvedUserDto>();
            }

            // Get user details by username
            List<ResolvedUserDto> users = await _unitOfWork.Users
                .GetAll()
                .AsNoTracking()
                .Where(u => u.UserName != null && allowedUsernames.Contains(u.UserName))
                .Select(u => new ResolvedUserDto
                {
                    Username = u.UserName!,
                    FullName = (u.FirstName != null || u.LastName != null)
                        ? ((u.FirstName ?? string.Empty) + " " + (u.MiddleName != null ? u.MiddleName + " " : string.Empty) + (u.LastName ?? string.Empty)).Trim()
                        : null,
                })
                .ToListAsync(cancellationToken);

            _logger.LogDebug(
                "Resolved {Count} users for tenant {OrganizationId}",
                users.Count,
                request.OrganizationId);

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error resolving usernames for tenant {OrganizationId}",
                request.OrganizationId);
            return Error.Failure(description: "An error occurred while resolving users");
        }
    }
}
