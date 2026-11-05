using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.Admin.Pages;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace IniaAi.WebApp.Tests.Components.Admin;

/// <summary>
/// An organization admin manages the accounts of their organization: they see each user's
/// organizations, grant only organization roles, and leave alone a user who holds a role that
/// reaches every organization. Restarting the application stays with super admins.
/// </summary>
public class AdministrationTests : SignedInRenderTest
{
    private const string ADMIN = "admin@example.test";
    private const string ANALYST = "analyst@example.test";
    private const string STAFF = "staff@example.test";
    private const string SHARED = "shared@example.test";
    private const string OTHER_ORG_ID = "org-other";
    private const string OTHER_ORG_NAME = "Other Customer";
    private const string LOCKED = "Only a super administrator manages this user";
    private const string RESTART = "Restart Application";
    private const string ACTIONS = "Administration actions";
    private const string IMPORT_USERS = "Import Users";
    private const string CREATE_TENANT = "Create Tenant";
    private const string NEW_TENANT_ID = "example-bank";
    private const string NEW_TENANT_NAME = "Example Bank";

    public AdministrationTests()
        : base(new ServerLikeQueryInterceptor())
    {
    }

    [Fact]
    public async Task ASuperAdmin_SeesTheTenantsOnFirstLoad()
    {
        _ = await SignInAsync(STAFF, Roles.USER, Roles.SUPER_ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();

        page.WaitForAssertion(() => Assert.True(Shows(page, $"Edit tenant {ORGANIZATION_NAME}")));
    }

    [Fact]
    public async Task ASuperAdmin_CreatesATenant()
    {
        _ = await SignInAsync(STAFF, Roles.USER, Roles.SUPER_ADMIN);
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<Administration> page = RenderInApp<Administration>();
        page.WaitForAssertion(() => Assert.True(Shows(page, $"Edit tenant {ORGANIZATION_NAME}")));

        await page.InvokeAsync(() => page.FindAll("button").First(button => button.TextContent.Trim() == CREATE_TENANT).Click());
        dialogs.WaitForAssertion(() => Assert.NotNull(DialogField(dialogs, "ID")));
        await dialogs.InvokeAsync(() => DialogField(dialogs, "ID")!.Input(NEW_TENANT_ID));
        await dialogs.InvokeAsync(() => DialogField(dialogs, "Name")!.Input(NEW_TENANT_NAME));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(button => button.TextContent.Trim() == "Create").Click());

        page.WaitForAssertion(() => Assert.True(Shows(page, $"Edit tenant {NEW_TENANT_NAME}")));
    }

    [Fact]
    public async Task AnAdmin_SeesTheOrganizationsOfEachUser()
    {
        _ = await SeedUserAsync(ANALYST, Roles.USER);
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();

        page.WaitForAssertion(() => Assert.Equal(ORGANIZATION_NAME, Cell(page, ANALYST, "Organizations").TextContent.Trim()));
    }

    [Fact]
    public async Task AUserRow_AppearsWithItsOrganizations_NeverBeforeThem()
    {
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();

        page.WaitForAssertion(() => Assert.True(Shows(page, $"Edit user info of {ADMIN}")));
        Assert.Equal(ORGANIZATION_NAME, Cell(page, ADMIN, "Organizations").TextContent.Trim());
    }

    [Fact]
    public async Task AnAdmin_LeavesAUserWithADeploymentWideRoleToSuperAdmins()
    {
        _ = await SeedUserAsync(ANALYST, Roles.USER);
        _ = await SeedUserAsync(STAFF, Roles.USER, Roles.WORKFLOW_EDITOR, Roles.OPERATOR);
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();

        page.WaitForAssertion(() => Assert.True(Shows(page, $"{STAFF}: {LOCKED}")));
        Assert.False(Shows(page, $"Edit user info of {STAFF}"));
        Assert.True(Shows(page, $"Edit user info of {ANALYST}"));
        Assert.True(Shows(page, $"Edit user info of {ADMIN}"));
        Assert.False(Shows(page, $"{ADMIN}: {LOCKED}"));
    }

    [Fact]
    public async Task AnAdmin_LeavesAUserWhoAlsoBelongsToAnotherOrganizationToSuperAdmins()
    {
        User shared = await SeedUserAsync(SHARED, Roles.USER);
        await SeedMembershipAsync(shared, OTHER_ORG_ID, OTHER_ORG_NAME);
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();

        page.WaitForAssertion(() => Assert.True(Shows(page, $"{SHARED}: {LOCKED}")));
        Assert.False(Shows(page, $"Reset password of {SHARED}"));
    }

    [Fact]
    public async Task AnAdmin_SeesOnlyTheirOwnOrganizationsOfAUser()
    {
        User shared = await SeedUserAsync(SHARED, Roles.USER);
        await SeedMembershipAsync(shared, OTHER_ORG_ID, OTHER_ORG_NAME);
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();

        page.WaitForAssertion(() => Assert.Equal(ORGANIZATION_NAME, Cell(page, SHARED, "Organizations").TextContent.Trim()));
        Assert.DoesNotContain(OTHER_ORG_NAME, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAdmin_IsOfferedOrganizationRolesOnly()
    {
        _ = await SeedUserAsync(ANALYST, Roles.USER);
        _ = await SeedUserAsync(STAFF, Roles.USER, Roles.OPERATOR);
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();
        page.WaitForAssertion(() => Assert.NotNull(Row(page, ANALYST)));
        await page.InvokeAsync(() => page.Find("[role=tab]:not([aria-selected=true])").Click());
        page.WaitForAssertion(() => Assert.NotNull(RoleButton(page, ANALYST)));

        Assert.NotNull(Row(page, STAFF));
        Assert.Null(RoleButton(page, STAFF));
        await page.InvokeAsync(() => RoleButton(page, ANALYST)!.Click());

        RoleGrants grants = Services.GetRequiredService<RoleGrants>();
        string[] expected = [.. grants.OrganizationRoles.Where(role => role != Roles.USER).Select(grants.DisplayName).Order()];
        Popovers.WaitForAssertion(() => Assert.Equal(expected, OfferedRoles()));
    }

    [Fact]
    public async Task AnAdmin_CannotRestartTheApplication()
    {
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();

        page.WaitForAssertion(() => Assert.True(Shows(page, $"Edit user info of {ADMIN}")));
        Assert.False(Shows(page, RESTART));
    }

    [Fact]
    public async Task AnAdmin_ImportsUsersFromTheHeader()
    {
        _ = await SignInAsync(ADMIN, Roles.USER, Roles.ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();
        page.WaitForAssertion(() => Assert.True(Shows(page, $"Edit user info of {ADMIN}")));

        await page.InvokeAsync(() => page.Find($".admin-header button[aria-label='{ACTIONS}']").Click());

        Popovers.WaitForAssertion(() => Assert.Contains(
            IMPORT_USERS,
            Popovers.FindAll(".mud-popover-open .mud-menu-item-text").Select(item => item.TextContent.Trim())));
    }

    [Fact]
    public async Task ASuperAdmin_CanRestartTheApplication()
    {
        _ = await SignInAsync(STAFF, Roles.USER, Roles.SUPER_ADMIN);

        IRenderedComponent<Administration> page = RenderInApp<Administration>();

        Assert.True(Shows(page, RESTART));
    }

    private string[] OfferedRoles()
    {
        return [.. Popovers.FindAll(".mud-popover-open .mud-menu-item-text").Select(item => item.TextContent.Trim()).Order()];
    }

    private static IElement? DialogField(IRenderedComponent<MudDialogProvider> dialogs, string label)
    {
        return dialogs.FindAll(".mud-input-control")
            .FirstOrDefault(control => control.QuerySelector("label")?.TextContent.Trim().TrimEnd('*') == label)
            ?.QuerySelector("input");
    }

    private static IElement? Row(IRenderedComponent<Administration> page, string email)
    {
        return page.FindAll("tr").FirstOrDefault(row => row.QuerySelectorAll("td, span").Any(cell => cell.TextContent.Trim() == email));
    }

    private static IElement Cell(IRenderedComponent<Administration> page, string email, string column)
    {
        IElement row = Row(page, email) ?? throw new InvalidOperationException($"No row for {email}.");
        return row.QuerySelector($"td[data-label='{column}']") ?? throw new InvalidOperationException($"No {column} cell for {email}.");
    }

    private static IElement? RoleButton(IRenderedComponent<Administration> page, string email)
    {
        return Row(page, email)?.QuerySelectorAll("button").FirstOrDefault(button => button.TextContent.Trim() == "Role");
    }
}
