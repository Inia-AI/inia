using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApp.Components.Admin.Dialogs;
using IniaAi.WebApp.Components.Admin.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApp.Tests.Components.Admin;

/// <summary>
/// Taking access away from a user in the admin app ends their API sessions at once: the screens
/// that remove a role or a membership revoke every stored token of that user.
/// </summary>
public class AccessRemovalRevocationTests : SignedInRenderTest
{
    private const string SUPER_ADMIN = "root@example.test";
    private const string TARGET = "target@example.test";
    private const string OTHER_ORG_ID = "org-other";
    private const string OTHER_ORG_NAME = "Other Customer";

    [Fact]
    public async Task RemovingARole_RevokesTheUsersTokens()
    {
        User target = await SeedUserAsync(TARGET, Roles.USER, Roles.ADMIN);
        await SeedTokenAsync(target.Id);
        _ = await SignInAsync(SUPER_ADMIN, Roles.USER, Roles.SUPER_ADMIN);
        IRenderedComponent<Administration> page = RenderInApp<Administration>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".mud-tab")));

        string adminRole = Services.GetRequiredService<RoleGrants>().DisplayName(Roles.ADMIN);
        page.FindAll(".mud-tab").First(tab => tab.TextContent.Trim() == "Roles").Click();
        page.WaitForAssertion(() => Assert.NotNull(RoleChip(page, target.Email!, adminRole)));
        RoleChip(page, target.Email!, adminRole)!.QuerySelector("button")!.Click();

        await WaitUntilRevokedAsync(page, target.Id);
    }

    [Fact]
    public async Task RemovingAMembership_RevokesTheUsersTokens()
    {
        User target = await SeedUserAsync(TARGET, Roles.USER);
        await SeedMembershipAsync(target, OTHER_ORG_ID, OTHER_ORG_NAME);
        await SeedTokenAsync(target.Id);
        _ = await SignInAsync(SUPER_ADMIN, Roles.USER, Roles.SUPER_ADMIN);
        UserDataDbContext db = Services.GetRequiredService<UserDataDbContext>();
        List<UserOrganization> memberships = await db.UserOrganizations.Where(membership => membership.UserId == target.Id).ToListAsync(Xunit.TestContext.Current.CancellationToken);

        _ = Render<MudPopoverProvider>();
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        _ = await dialogs.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ShowAsync<UserOrganizationManagementDialog>(
            "Manage User Organizations",
            new DialogParameters<UserOrganizationManagementDialog>
            {
                { x => x.User, target },
                { x => x.UserOrganizations, memberships },
            }));
        dialogs.WaitForAssertion(() => Assert.NotEmpty(Buttons(dialogs, "Remove")));

        Buttons(dialogs, "Remove").First().Click();
        Buttons(dialogs, "Save Changes").Single().Click();

        await WaitUntilRevokedAsync(dialogs, target.Id);
    }

    private async Task SeedTokenAsync(string userId)
    {
        await SeedAsync(new OpenIddictEntityFrameworkCoreToken { Subject = userId, Status = Statuses.Valid, Type = TokenTypeIdentifiers.AccessToken });
    }

    private async Task WaitUntilRevokedAsync<TComponent>(IRenderedComponent<TComponent> rendered, string userId)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        UserDataDbContext db = Services.GetRequiredService<UserDataDbContext>();
        await rendered.WaitForStateAsync(
            () => db.Tokens.AsNoTracking().Where(token => token.Subject == userId).All(token => token.Status == Statuses.Revoked),
            TimeSpan.FromSeconds(10));
    }

    private static IElement? RoleChip(IRenderedComponent<Administration> page, string email, string roleName)
    {
        return page.FindAll("tr")
            .FirstOrDefault(row => row.TextContent.Contains(email, StringComparison.Ordinal))?
            .QuerySelectorAll(".mud-chip")
            .FirstOrDefault(chip => chip.TextContent.Trim() == roleName);
    }

    private static List<IElement> Buttons(IRenderedComponent<MudDialogProvider> dialogs, string text)
    {
        return [.. dialogs.FindAll("button").Where(button => button.TextContent.Trim() == text)];
    }
}
