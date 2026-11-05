using System.Security.Claims;
using IniaAi.Application.Workflows.EditorAgent;
using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <summary>
/// Who may use the workflow agent: the editor roles, on a deployment that turned the agent on.
/// Some editor pages also admit monitoring roles, so the page's own gate is not enough.
/// </summary>
public static class EditorAgentAccess
{
    public static bool CanUse(ClaimsPrincipal? user, IWorkflowEditorAgentService agent)
    {
        return agent.IsEnabled
            && user is not null
            && (user.IsInRole(Roles.WORKFLOW_EDITOR) || user.IsInRole(Roles.SUPER_ADMIN));
    }
}
