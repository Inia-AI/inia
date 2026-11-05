using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Organizations;

namespace IniaAi.Application.Queries.Organizations.GetUserTenants;

/// <summary>
/// Query to retrieve all active organizations (tenants) that a specific user belongs to.
/// Returns only the basic tenant information (ID and Name) suitable for tenant selection.
/// </summary>
/// <param name="UserId">The unique identifier of the user to get tenants for.</param>
public record GetUserTenantsQuery(string UserId) : IRequest<ErrorOr<List<UserTenantDto>>>;
