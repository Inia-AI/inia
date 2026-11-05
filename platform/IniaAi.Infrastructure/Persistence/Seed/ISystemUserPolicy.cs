using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Infrastructure.Persistence.Seed;

/// <summary>
/// Decides which organizations <see cref="SystemUserSeeder"/> gives a system user. A host that
/// registers none gives every organization one, as <see cref="SystemUserForEveryOrganization"/> does.
/// </summary>
public interface ISystemUserPolicy
{
    bool SeedsSystemUser(Organization organization);
}

public sealed class SystemUserForEveryOrganization : ISystemUserPolicy
{
    public bool SeedsSystemUser(Organization organization)
    {
        return true;
    }
}
