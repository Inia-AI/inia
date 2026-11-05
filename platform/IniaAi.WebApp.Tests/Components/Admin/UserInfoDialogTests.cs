using AngleSharp.Dom;
using Bunit;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.Admin.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace IniaAi.WebApp.Tests.Components.Admin;

/// <summary>
/// The admin app's Edit User dialog writes the account directly, not through UserManager, so it must
/// keep the user name to the characters Identity accepts and keep the names Identity looks up by.
/// </summary>
public class UserInfoDialogTests : SignedInRenderTest
{
    private const string EMAIL = "analyst@example.test";

    [Fact]
    public async Task AUserNameIdentityRejects_IsRefusedAndTheDialogStaysOpen()
    {
        User user = await SeedUserAsync(EMAIL, Roles.USER);
        (IRenderedComponent<MudDialogProvider> dialogs, IDialogReference dialog) = await OpenAsync(user);

        UserNameField(dialogs).Change("Example Admin");
        SaveButton(dialogs).Click();

        dialogs.WaitForAssertion(() => Assert.Contains("letters, digits", dialogs.Markup));
        Assert.False(dialog.Result.IsCompleted);
    }

    [Fact]
    public async Task ANewUserNameAndEmail_SaveWithTheNamesIdentityLooksUpBy()
    {
        User user = await SeedUserAsync(EMAIL, Roles.USER);
        (IRenderedComponent<MudDialogProvider> dialogs, IDialogReference dialog) = await OpenAsync(user);

        UserNameField(dialogs).Change("renamed@example.test");
        EmailField(dialogs).Change("Renamed@Example.test");
        SaveButton(dialogs).Click();

        DialogResult? result = await dialog.Result;
        Assert.False(result!.Canceled);
        Assert.Equal("RENAMED@EXAMPLE.TEST", user.NormalizedUserName);
        Assert.Equal("RENAMED@EXAMPLE.TEST", user.NormalizedEmail);
    }

    private async Task<(IRenderedComponent<MudDialogProvider>, IDialogReference)> OpenAsync(User user)
    {
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IDialogService service = Services.GetRequiredService<IDialogService>();
        IDialogReference dialog = await dialogs.InvokeAsync(() => service.ShowAsync<UserInfoDialog>(
            "Edit User",
            new DialogParameters<UserInfoDialog> { { x => x.User, user } }));
        dialogs.WaitForAssertion(() => Assert.NotNull(SaveButton(dialogs)));
        return (dialogs, dialog);
    }

    private static IElement UserNameField(IRenderedComponent<MudDialogProvider> dialogs)
    {
        return Field(dialogs, "Username");
    }

    private static IElement EmailField(IRenderedComponent<MudDialogProvider> dialogs)
    {
        return Field(dialogs, "Email");
    }

    private static IElement Field(IRenderedComponent<MudDialogProvider> dialogs, string label)
    {
        return dialogs.FindAll(".mud-input-control")
            .First(control => control.QuerySelector("label")?.TextContent.Trim() == label)
            .QuerySelector("input")!;
    }

    private static IElement SaveButton(IRenderedComponent<MudDialogProvider> dialogs)
    {
        return dialogs.FindAll("button").First(button => button.TextContent.Trim() == "Save");
    }
}
