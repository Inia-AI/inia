using Ardalis.Specification;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Specifications.Common;

namespace IniaAi.Domain.Specifications.Users;

/// <summary>
/// Specification for retrieving active, non-system users within an organization.
/// Uses reusable expressions for clean composition.
/// Tracking behavior and ordering controlled by the caller.
/// </summary>
public sealed class ActiveNonSystemUsersByOrganizationSpec : Specification<User>
{
    public ActiveNonSystemUsersByOrganizationSpec(string organizationId)
    {
        _ = Query
            .Where(UserExpressions.ByOrganization(organizationId))
            .Where(UserExpressions.IsActive)
            .Where(UserExpressions.IsNotSystem);
    }
}