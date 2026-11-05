using Ardalis.Specification;
using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Domain.Specifications.Organizations;

/// <summary>
/// A user's active memberships in active organizations: the organizations the user may act in.
/// </summary>
public sealed class UserActiveMembershipsSpec : Specification<UserOrganization>
{
    public UserActiveMembershipsSpec(string userId)
    {
        _ = Query.Where(membership => membership.UserId == userId && membership.IsActive && membership.Organization.IsActive);
    }
}
