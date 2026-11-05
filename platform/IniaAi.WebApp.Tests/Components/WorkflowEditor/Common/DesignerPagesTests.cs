using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.WorkflowEditor.Common;
using IniaAi.WebApp.Components.WorkflowEditor.Pages;
using IniaAi.WebApp.Tests.Helpers;
using Microsoft.AspNetCore.Authorization;
using MudBlazor;
using System.Reflection;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Common;

/// <summary>
/// The Workflow Editors list admits every signed-in user and shows a tile for a workflow only
/// when the user's roles open that workflow's designer page. An editor opens every workflow; a
/// module's own page may also open for a role the module adds. An organization admin opens none,
/// because a template is shared by every organization that runs it.
/// </summary>
public class DesignerPagesTests
{
    private const string USER_ID = "user-1";
    private const string GENERIC_WORKFLOW_TYPE_ID = "Corp";
    private const string MODULE_WORKFLOW_TYPE_ID = "AuditTrail";
    private const string MODULE_ROLE = "Auditor";

    private static readonly DesignerPages _openBuild = new([]);
    private static readonly DesignerPages _withAModulePage = new(
        [new ModuleDesignerPage(MODULE_WORKFLOW_TYPE_ID, $"{Roles.EDITOR_ROLES},{MODULE_ROLE}", Icons.Material.Outlined.Gavel)]);

    [Theory]
    [InlineData(Roles.WORKFLOW_EDITOR)]
    [InlineData(Roles.SUPER_ADMIN)]
    public void AWorkflow_OpensForEachEditorRole(string role)
    {
        Assert.True(_openBuild.CanOpen(WebAppCallers.SignedIn(USER_ID, Roles.USER, role), GENERIC_WORKFLOW_TYPE_ID));
    }

    [Theory]
    [InlineData(Roles.USER)]
    [InlineData(Roles.ADMIN)]
    [InlineData(Roles.OPERATOR)]
    public void AWorkflow_DoesNotOpenForAUserWithoutAnEditorRole(string role)
    {
        Assert.False(_openBuild.CanOpen(WebAppCallers.SignedIn(USER_ID, Roles.USER, role), GENERIC_WORKFLOW_TYPE_ID));
    }

    [Theory]
    [InlineData(MODULE_ROLE, true)]
    [InlineData(Roles.WORKFLOW_EDITOR, true)]
    [InlineData(Roles.ADMIN, false)]
    public void AModulePage_OpensForItsRolesAndShowsItsIcon(string role, bool opens)
    {
        Assert.Equal(opens, _withAModulePage.CanOpen(WebAppCallers.SignedIn(USER_ID, Roles.USER, role), MODULE_WORKFLOW_TYPE_ID));
        Assert.Equal(Icons.Material.Outlined.Gavel, _withAModulePage.IconFor(MODULE_WORKFLOW_TYPE_ID));
    }

    [Fact]
    public void WithoutItsModule_AModuleTypeOpensInTheGenericPage()
    {
        Assert.Equal(DesignerPages.GENERIC_ROLES, _openBuild.RolesFor(MODULE_WORKFLOW_TYPE_ID));
        Assert.False(_openBuild.CanOpen(WebAppCallers.SignedIn(USER_ID, Roles.USER, MODULE_ROLE), MODULE_WORKFLOW_TYPE_ID));
        Assert.Equal(Icons.Material.Outlined.Description, _openBuild.IconFor(MODULE_WORKFLOW_TYPE_ID));
    }

    [Fact]
    public void TheGenericDesignerPage_GatesOnTheRolesTheListChecks()
    {
        AuthorizeAttribute? gate = typeof(WorkflowDesignerPage).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(gate);
        Assert.Equal(_openBuild.RolesFor(GENERIC_WORKFLOW_TYPE_ID), gate.Roles);
    }
}
