using AngleSharp.Dom;
using Bunit;
using IniaAi.WebApp.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace IniaAi.WebApp.Tests.Components;

/// <summary>
/// An address no page answers, typed or followed from an old link, shows the not-found page instead
/// of an empty screen.
/// </summary>
public class PageNotFoundTests : BunitContext
{
    public PageNotFoundTests()
    {
        // MudBlazor makes JavaScript calls of its own that no test here is about.
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Fact]
    public void NamesTheAddress_AndLeadsBackToTheDashboard()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/prompt-library?tab=prompts");

        IRenderedComponent<PageNotFound> page = Render<PageNotFound>();

        Assert.Equal("Page not found", page.Find("h1").TextContent.Trim());
        Assert.Equal("/prompt-library?tab=prompts", page.Find("code").TextContent);
        IElement home = page.Find("a");
        Assert.Equal("/", home.GetAttribute("href"));
        Assert.Equal("Go to the Dashboard", home.TextContent.Trim());
    }
}
