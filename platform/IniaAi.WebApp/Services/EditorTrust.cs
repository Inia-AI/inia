using IniaAi.Domain.Entities.UserManagement;
using System.Security.Claims;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Who the workflow editor trusts with what they upload and run: the people who build workflows.
/// A trusted user's uploads keep only the refusals that protect the server, and their editor
/// reports skip the screening unless they ask for it. Admins and the roles a module adds also
/// reach some editor pages, and a customer may hold those roles, so they keep the customer checks.
/// </summary>
public static class EditorTrust
{
    public static bool Extends(ClaimsPrincipal user)
    {
        return Roles.HeldBy(user, Roles.EDITOR_ROLES);
    }
}
