using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.UserWorkflows.Pages;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// Staff reach their daily pages without SuperAdmin. The executed-workflow list reads every
/// organization's runs, so it is an operations page.
/// </summary>
public class PageRoleGatesTests
{
    [Theory]
    [InlineData(typeof(Workflows), Roles.OPERATOR_ROLES)]
    public void ThePage_AdmitsItsRoles(Type page, string roles)
    {
        AuthorizeAttribute? gate = page.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(roles, gate.Roles);
    }
}
