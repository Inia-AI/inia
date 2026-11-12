using Microsoft.AspNetCore.Components;

namespace IniaAi.WebApp.Tests.Components.Account;

/// <summary>
/// Navigation as the server does it while it renders an account page statically: a redirect ends
/// the render with a <see cref="NavigationException"/>, which the server turns into a redirect.
/// </summary>
internal sealed class StaticRenderNavigationManager : NavigationManager
{
    public StaticRenderNavigationManager(string baseUrl)
    {
        Initialize($"{baseUrl}/", $"{baseUrl}/Account");
    }

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        throw new NavigationException(ToAbsoluteUri(uri).AbsoluteUri);
    }
}
