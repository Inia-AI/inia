using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Shared.Users;

namespace IniaAi.Application.Queries.Users.ResolveUsers;

/// <summary>
/// Query to resolve usernames to their display information (full name).
/// Only resolves users who have created workflow reports in the specified tenant.
/// </summary>
/// <param name="OrganizationId">The organization ID to scope the resolution.</param>
/// <param name="UserId">The user ID making the request.</param>
/// <param name="UsernamesToResolve">The list of usernames to resolve.</param>
public record ResolveUsersQuery(
    string OrganizationId,
    string UserId,
    List<string> UsernamesToResolve) : IRequest<ErrorOr<List<ResolvedUserDto>>>;
