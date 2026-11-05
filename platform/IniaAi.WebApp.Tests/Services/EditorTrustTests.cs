using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Services;
using IniaAi.WebApp.Tests.Helpers;

namespace IniaAi.WebApp.Tests.Services;

/// <summary>
/// The workflow editor waives the customer checks on uploads and runs only for the people who
/// build workflows. Admins and the roles a module adds also reach some editor pages, and a
/// customer may hold those roles, so they keep the checks customers get.
/// </summary>
public class EditorTrustTests
{
    private const string USER_ID = "user-1";

    [Theory]
    [InlineData(Roles.WORKFLOW_EDITOR)]
    [InlineData(Roles.SUPER_ADMIN)]
    public void TheEditorRoles_AreTrusted(string role)
    {
        Assert.True(EditorTrust.Extends(WebAppCallers.SignedIn(USER_ID, role)));
    }

    [Theory]
    [InlineData(Roles.ADMIN)]
    [InlineData(Roles.OPERATOR)]
    [InlineData(Roles.SIGNATURE_CONTRIBUTOR)]
    [InlineData(Roles.USER)]
    public void EveryOtherRole_KeepsTheCustomerChecks(string role)
    {
        Assert.False(EditorTrust.Extends(WebAppCallers.SignedIn(USER_ID, role)));
    }

    [Fact]
    public void AnAdminWhoIsAlsoAWorkflowEditor_IsTrusted()
    {
        Assert.True(EditorTrust.Extends(WebAppCallers.SignedIn(USER_ID, Roles.ADMIN, Roles.WORKFLOW_EDITOR)));
    }
}
