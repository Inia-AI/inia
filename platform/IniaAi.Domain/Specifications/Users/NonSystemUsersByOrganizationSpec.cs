using Ardalis.Specification;
using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Domain.Specifications.Users;

/// <summary>
/// Specification for retrieving non-system users within an organization.
/// Combines organization filtering with business rule (non-system users only).
/// </summary>
public sealed class NonSystemUsersByOrganizationSpec : Specification<User>
{
    public NonSystemUsersByOrganizationSpec(string organizationId)
    {
        _ = Query
            .Where(e => e.OrganizationId == organizationId && !e.IsSystem)
            .AsNoTracking();
    }
}