using ErrorOr;
using IniaAi.Application.Common.Capabilities;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Specifications.Organizations;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.Organizations.GetUserTenants;

/// <summary>
/// Handles the query to retrieve active organizations (tenants) that a user belongs to.
/// </summary>
public class GetUserTenantsQueryHandler : IRequestHandler<GetUserTenantsQuery, ErrorOr<List<UserTenantDto>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly IOrganizationCapabilities _capabilities;
    private readonly ILogger<GetUserTenantsQueryHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetUserTenantsQueryHandler"/> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work for data access.</param>
    /// <param name="capabilities">What each organization may do.</param>
    /// <param name="logger">The logger instance.</param>
    public GetUserTenantsQueryHandler(
        IUserDataUnitOfWork unitOfWork,
        IOrganizationCapabilities capabilities,
        ILogger<GetUserTenantsQueryHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _capabilities = capabilities;
        _logger = logger;
    }

    /// <summary>
    /// Handles the query request to retrieve active organizations for a user.
    /// </summary>
    /// <param name="request">The query request containing the user ID.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of user tenant DTOs or an error.</returns>
    public async Task<ErrorOr<List<UserTenantDto>>> Handle(
        GetUserTenantsQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            List<UserTenantDto> result = await _unitOfWork.Repository<UserOrganization>()
                .GetAll(new UserActiveMembershipsSpec(request.UserId))
                .OrderBy(membership => membership.Organization.Name)
                .Select(membership => new UserTenantDto { Id = membership.Organization.Id, Name = membership.Organization.Name })
                .ToListAsync(cancellationToken);

            foreach (UserTenantDto tenant in result)
            {
                IReadOnlySet<string> capabilities = await _capabilities.GetAsync(tenant.Id, cancellationToken);
                tenant.Capabilities = [.. capabilities.Order(StringComparer.Ordinal)];
            }

            _logger.LogInformation(
                "Retrieved {Count} tenants for user {UserId}",
                result.Count,
                request.UserId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user tenants for user {UserId}", request.UserId);
            return Error.Failure(description: "An error occurred while retrieving tenants");
        }
    }
}
