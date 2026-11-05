using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.Account.Shared;
using System.Security.Claims;

namespace IniaAi.WebApp.Tests.Components.Account;

/// <summary>
/// A page the user's roles do not open sends a signed-in user to Access denied, the same screen
/// the server gives for a direct visit. Only a visitor who is not signed in sees the sign-in form.
/// </summary>
public class RedirectUnauthorizedTests
{
    private const string DESIGNER_URI = "https://localhost:7119/editors/HedgeFundsMonitoring";

    [Fact]
    public void ASignedInUserWithoutTheRole_GoesToAccessDenied()
    {
        var workflowEditor = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "editor@example.test"), new Claim(ClaimTypes.Role, Roles.WORKFLOW_EDITOR)],
            "Identity.Application"));

        Assert.Equal("Account/AccessDenied", RedirectUnauthorized.TargetFor(workflowEditor, DESIGNER_URI));
    }

    [Fact]
    public void AVisitorWhoIsNotSignedIn_GoesToTheSignInFormWithTheReturnAddress()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal(
            $"Account/Login?returnUrl={Uri.EscapeDataString(DESIGNER_URI)}",
            RedirectUnauthorized.TargetFor(anonymous, DESIGNER_URI));
    }
}
