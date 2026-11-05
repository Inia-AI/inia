using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.WorkflowTypes.Pages;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

namespace IniaAi.WebApp.Tests.Components.WorkflowTypes;

/// <summary>
/// Workflow Type Management is for editors, who author workflow types and categories, and for
/// operators, who choose which organizations may run a type. Workflow types are shared by every
/// organization, so an organization admin does not reach the page. The left menu shows its entry to
/// exactly the roles the page admits, so no role reaches the page only by typing its URL.
/// </summary>
public class WorkflowTypeManagementRolesTests
{
    [Fact]
    public void ThePageGate_IsTheRoleListTheMenuShowsTheEntryTo()
    {
        AuthorizeAttribute? gate = typeof(WorkflowTypeManagement).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(WorkflowTypeManagement.ROLES, gate.Roles);
    }

    [Fact]
    public void ThePage_AdmitsEditorsAndOperators()
    {
        Assert.Equal(
            [Roles.OPERATOR, Roles.SUPER_ADMIN, Roles.WORKFLOW_EDITOR],
            WorkflowTypeManagement.ROLES.Split(',').Order(StringComparer.Ordinal));
    }
}
