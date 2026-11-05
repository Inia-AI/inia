using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Components.Layout;
using Microsoft.AspNetCore.Components;

namespace IniaAi.WebApp.Tests.Components.Layout;

/// <summary>
/// The Account pages render without a live connection, so nothing in the app bar that needs one can
/// work there. The layout shows no such control on them, and its Account menu still opens and logs
/// the user out with plain HTML.
/// </summary>
public class MainLayoutStaticRenderTests : SignedInRenderTest
{
    private const string EMAIL = "someone@example.test";
    private const string ACCOUNT_MENU = "Account menu";

    [Fact]
    public async Task AStaticPage_ShowsNoAppBarControlThatNeedsALiveConnection()
    {
        _ = await SignInAsync(EMAIL, Roles.USER, Roles.OPERATOR);
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        IRenderedComponent<MainLayout> layout = Render<MainLayout>();

        Assert.False(Shows(layout, "Toggle navigation menu"));
        Assert.False(Shows(layout, "Switch to light mode"));
        Assert.False(Shows(layout, "Switch to dark mode"));
        Assert.False(Shows(layout, "Log"));
        Assert.Empty(layout.FindAll(".appbar-tenant"));
    }

    [Fact]
    public async Task AStaticPage_AccountMenu_OpensWithoutScript_AndLogsOut()
    {
        _ = await SignInAsync(EMAIL, Roles.USER);
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        IRenderedComponent<MainLayout> layout = Render<MainLayout>();

        IElement menu = layout.Find($"details > summary[aria-label='{ACCOUNT_MENU}']").ParentElement!;
        Assert.Equal("Account/Manage", menu.QuerySelector("a")!.GetAttribute("href"));
        IElement logout = menu.QuerySelectorAll("form button[type=submit]").Single(button => button.TextContent.Trim() == "Logout");
        IElement form = logout.Closest("form")!;
        Assert.Equal("Account/Logout", form.GetAttribute("action"));
        Assert.Equal("post", form.GetAttribute("method"));
    }

    [Fact]
    public async Task AnInteractivePage_KeepsTheFullAppBar()
    {
        _ = await SignInAsync(EMAIL, Roles.USER);
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));

        IRenderedComponent<MainLayout> layout = Render<MainLayout>();

        Assert.True(Shows(layout, "Toggle navigation menu"));
        Assert.True(Shows(layout, ACCOUNT_MENU));
        Assert.Empty(layout.FindAll("details > summary"));
    }
}
