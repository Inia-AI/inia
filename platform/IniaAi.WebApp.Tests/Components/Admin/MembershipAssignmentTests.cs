using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApp.Components.Admin.Dialogs;
using IniaAi.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace IniaAi.WebApp.Tests.Components.Admin;

/// <summary>
/// A membership an admin adds in the admin app records who added it, so an operator's access to an
/// organization can be traced to the person who granted it.
/// </summary>
public class MembershipAssignmentTests : SignedInRenderTest
{
    private const string SUPER_ADMIN = "root@example.test";
    private const string TARGET = "target@example.test";
    private const string OTHER_ORG_ID = "org-other";

    [Fact]
    public async Task AMembershipAddedInTheDialog_RecordsTheAdminWhoAddedIt()
    {
        User target = await SeedUserAsync(TARGET, Roles.USER);
        await SeedAsync(new Organization { Id = OTHER_ORG_ID, Name = "Other Customer" });
        User admin = await SignInAsync(SUPER_ADMIN, Roles.USER, Roles.SUPER_ADMIN);
        AppState appState = Services.GetRequiredService<AppState>();
        await appState.ReloadUserAsync();

        _ = Render<MudPopoverProvider>();
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        _ = await dialogs.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ShowAsync<UserOrganizationManagementDialog>(
            "Manage User Organizations",
            new DialogParameters<UserOrganizationManagementDialog>
            {
                { x => x.User, target },
                { x => x.UserOrganizations, new List<UserOrganization>() },
            }));
        IRenderedComponent<MudSelect<string>> select = dialogs.FindComponent<MudSelect<string>>();
        await select.InvokeAsync(() => select.Instance.SelectOption(OTHER_ORG_ID));
        dialogs.WaitForAssertion(() => Assert.False(Button(dialogs, "Add").HasAttribute("disabled")));

        Button(dialogs, "Add").Click();
        dialogs.WaitForAssertion(() => Assert.False(Button(dialogs, "Save Changes").HasAttribute("disabled")));
        Button(dialogs, "Save Changes").Click();

        UserDataDbContext db = Services.GetRequiredService<UserDataDbContext>();
        await dialogs.WaitForStateAsync(
            () => db.UserOrganizations.AsNoTracking().Any(membership => membership.UserId == target.Id && membership.OrganizationId == OTHER_ORG_ID && membership.AssignedByUserId == admin.Id),
            TimeSpan.FromSeconds(10));
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<MudDialogProvider> dialogs, string text)
    {
        return dialogs.FindAll("button").Single(button => button.TextContent.Trim() == text);
    }
}
