namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// Who may grant which role, and whose account an administrator may manage. Roles are global per
/// user, so a role granted in one organization applies in every organization the user belongs to.
/// An organization admin grants only the roles that act within the organizations a user belongs to;
/// only a super admin grants a role that reaches every organization of the deployment. The
/// platform's roles are fixed; a closed module adds its own (<see cref="ModuleRole"/>).
/// </summary>
public sealed class RoleGrants
{
    private static readonly string[] _platformRoles =
    [
        Roles.SUPER_ADMIN,
        Roles.ADMIN,
        Roles.USER,
        Roles.WORKFLOW_EDITOR,
        Roles.SIGNATURE_CONTRIBUTOR,
        Roles.OPERATOR,
    ];

    private static readonly string[] _platformOrganizationRoles =
    [
        Roles.USER,
        Roles.ADMIN,
        Roles.SIGNATURE_CONTRIBUTOR,
    ];

    private readonly HashSet<string> _deploymentWideRoles;
    private readonly Dictionary<string, string> _displayNames;

    public RoleGrants(IEnumerable<ModuleRole> moduleRoles)
    {
        IReadOnlyList<ModuleRole> modules = [.. moduleRoles.DistinctBy(role => role.Name)];

        DefinedRoles = [.. _platformRoles, .. modules.Select(role => role.Name)];
        OrganizationRoles = [.. _platformOrganizationRoles, .. modules.Where(role => role.ActsWithinOrganizations).Select(role => role.Name)];
        CustomerAdminModuleRoles = [.. modules.Where(role => role.GrantedToTheCustomerAdmin).Select(role => role.Name)];
        _deploymentWideRoles = [.. DefinedRoles.Except(OrganizationRoles)];
        _displayNames = modules.ToDictionary(role => role.Name, role => role.DisplayName, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> DefinedRoles { get; }

    public IReadOnlyList<string> OrganizationRoles { get; }

    /// <summary>The module roles the seed grants to the customer admin it creates.</summary>
    public IReadOnlyList<string> CustomerAdminModuleRoles { get; }

    public bool CanGrant(bool granterIsSuperAdmin, string roleName)
    {
        return granterIsSuperAdmin ? DefinedRoles.Contains(roleName) : OrganizationRoles.Contains(roleName);
    }

    /// <summary>The name the administration screens show for a stored role name.</summary>
    public string DisplayName(string roleName)
    {
        return _displayNames.GetValueOrDefault(roleName, roleName);
    }

    /// <summary>
    /// A super admin manages anyone. An organization admin manages a user only when the user holds no
    /// role that reaches every organization and belongs to no organization outside the admin's: a role
    /// or a password the admin sets would act in every organization the user belongs to. A role left in
    /// the database that the code no longer defines gates nothing, so it does not block management.
    /// </summary>
    public bool CanManage(
        bool managerIsSuperAdmin,
        IReadOnlySet<string> managerOrganizationIds,
        IEnumerable<string> targetRoleNames,
        IEnumerable<string> targetOrganizationIds)
    {
        return managerIsSuperAdmin
            || (!targetRoleNames.Any(_deploymentWideRoles.Contains) && targetOrganizationIds.All(managerOrganizationIds.Contains));
    }

    /// <summary>
    /// The users a manager may not act on, from every (user, role name) assignment and every active
    /// (user, organization) membership. The rule guards other people's accounts, so the manager's own
    /// account is never on the list.
    /// </summary>
    public IReadOnlySet<string> Unmanageable(
        bool managerIsSuperAdmin,
        string managerId,
        IReadOnlySet<string> managerOrganizationIds,
        IEnumerable<(string UserId, string? RoleName)> assignments,
        IEnumerable<(string UserId, string OrganizationId)> memberships)
    {
        ILookup<string, string> roles = assignments
            .Where(assignment => assignment.RoleName is not null)
            .ToLookup(assignment => assignment.UserId, assignment => assignment.RoleName!);
        ILookup<string, string> organizations = memberships.ToLookup(membership => membership.UserId, membership => membership.OrganizationId);

        return roles.Select(user => user.Key)
            .Union(organizations.Select(user => user.Key))
            .Where(userId => userId != managerId
                && !CanManage(managerIsSuperAdmin, managerOrganizationIds, roles[userId], organizations[userId]))
            .ToHashSet();
    }
}
