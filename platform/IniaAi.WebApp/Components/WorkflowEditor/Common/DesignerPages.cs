using IniaAi.Domain.Entities.UserManagement;
using MudBlazor;
using System.Security.Claims;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// The roles that open each workflow's designer page, and the icon of its tile. Every workflow
/// opens in the generic designer page for the editor roles, unless a module adds a page of its own
/// for the type. The pages gate on these roles and the Workflow Editors list checks the same ones,
/// so the list shows a tile only when its page opens.
/// </summary>
public sealed class DesignerPages
{
    public const string GENERIC_ROLES = Roles.EDITOR_ROLES;

    private readonly Dictionary<string, ModuleDesignerPage> _modulePages;

    public DesignerPages(IEnumerable<ModuleDesignerPage> modulePages)
    {
        _modulePages = modulePages.ToDictionary(page => page.WorkflowTypeId, StringComparer.Ordinal);
    }

    public string RolesFor(string workflowTypeId)
    {
        return _modulePages.TryGetValue(workflowTypeId, out ModuleDesignerPage? page) ? page.Roles : GENERIC_ROLES;
    }

    public string IconFor(string workflowTypeId)
    {
        return _modulePages.TryGetValue(workflowTypeId, out ModuleDesignerPage? page) ? page.Icon : Icons.Material.Outlined.Description;
    }

    public bool CanOpen(ClaimsPrincipal user, string workflowTypeId)
    {
        return Roles.HeldBy(user, RolesFor(workflowTypeId));
    }
}
