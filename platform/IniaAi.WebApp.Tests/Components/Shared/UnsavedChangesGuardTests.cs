using AngleSharp.Dom;
using Bunit;
using IniaAi.Testing;
using IniaAi.WebApp.Components.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace IniaAi.WebApp.Tests.Components.Shared;

/// <summary>
/// An editor page with unsaved changes asks before a link inside the app takes the author away, and
/// staying keeps the page and its edits.
/// </summary>
public class UnsavedChangesGuardTests : BunitContext
{
    private const string ELSEWHERE = "editors";

    public UnsavedChangesGuardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Fact]
    public void ALinkWithUnsavedChanges_AsksFirst_AndStayKeepsThePage()
    {
        IRenderedComponent<MudDialogProvider> dialogs = RenderGuard(isDirty: true);
        string start = Navigation.Uri;

        Navigation.NavigateTo(ELSEWHERE);
        dialogs.WaitForAssertion(() => Assert.Contains(UnsavedChangesGuard.MESSAGE, dialogs.Markup));
        Button(dialogs, UnsavedChangesGuard.STAY).Click();

        dialogs.WaitForAssertion(() => Assert.DoesNotContain(UnsavedChangesGuard.MESSAGE, dialogs.Markup));
        Assert.Equal(start, Navigation.Uri);
    }

    [Fact]
    public async Task LeavingWithoutSaving_FollowsTheLink()
    {
        IRenderedComponent<MudDialogProvider> dialogs = RenderGuard(isDirty: true);

        Navigation.NavigateTo(ELSEWHERE);
        dialogs.WaitForAssertion(() => Assert.Contains(UnsavedChangesGuard.MESSAGE, dialogs.Markup));
        Button(dialogs, UnsavedChangesGuard.LEAVE).Click();

        await ArrivedElsewhereAsync();
    }

    [Fact]
    public async Task WithNothingUnsaved_TheLinkIsFollowedWithoutAQuestion()
    {
        IRenderedComponent<MudDialogProvider> dialogs = RenderGuard(isDirty: false);

        Navigation.NavigateTo(ELSEWHERE);

        await ArrivedElsewhereAsync();
        Assert.DoesNotContain(UnsavedChangesGuard.MESSAGE, dialogs.Markup);
    }

    [Fact]
    public void TheExactCheck_Asks_WhenTheDirtyFlagHasNotCaughtUp()
    {
        IRenderedComponent<MudDialogProvider> dialogs = RenderGuard(isDirty: false, isDirtyNow: () => true);
        string start = Navigation.Uri;

        Navigation.NavigateTo(ELSEWHERE);
        dialogs.WaitForAssertion(() => Assert.Contains(UnsavedChangesGuard.MESSAGE, dialogs.Markup));
        Button(dialogs, UnsavedChangesGuard.STAY).Click();

        dialogs.WaitForAssertion(() => Assert.DoesNotContain(UnsavedChangesGuard.MESSAGE, dialogs.Markup));
        Assert.Equal(start, Navigation.Uri);
    }

    [Fact]
    public async Task AFlagThatLagsBehindASave_DoesNotAsk_WhenTheLiveStateIsClean()
    {
        IRenderedComponent<MudDialogProvider> dialogs = RenderGuard(isDirty: true, isDirtyNow: () => false);

        Navigation.NavigateTo(ELSEWHERE);

        await ArrivedElsewhereAsync();
        Assert.DoesNotContain(UnsavedChangesGuard.MESSAGE, dialogs.Markup);
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    /// <summary>
    /// A finished navigation renders nothing, so a wait that rechecks on renders could miss it, and
    /// the test navigation manager raises no event once a guard lets the navigation through.
    /// </summary>
    private Task ArrivedElsewhereAsync()
    {
        return Eventually.UntilAsync(
            () => Navigation.Uri.EndsWith(ELSEWHERE, StringComparison.Ordinal),
            "the link was followed",
            BunitContext.DefaultWaitTimeout,
            Xunit.TestContext.Current.CancellationToken);
    }

    private IRenderedComponent<MudDialogProvider> RenderGuard(bool isDirty, Func<bool>? isDirtyNow = null)
    {
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        _ = Render<UnsavedChangesGuard>(parameters => parameters
            .Add(guard => guard.IsDirty, isDirty)
            .Add(guard => guard.IsDirtyNow, isDirtyNow ?? (() => isDirty)));

        return dialogs;
    }

    private static IElement Button(IRenderedComponent<MudDialogProvider> dialogs, string text)
    {
        return dialogs.FindAll("button").First(button => button.TextContent.Trim() == text);
    }
}
