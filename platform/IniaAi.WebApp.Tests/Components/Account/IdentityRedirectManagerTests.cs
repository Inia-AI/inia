using IniaAi.WebApp.Components.Account;
using IniaAi.WebApp.Configuration;
using Microsoft.AspNetCore.Components;

namespace IniaAi.WebApp.Tests.Components.Account;

/// <summary>
/// After a sign-in, a second factor or a recovery code, the account pages send the browser to the
/// return address of the sign-in link. Anyone can write that link, so the address must keep the
/// browser on the admin app.
/// </summary>
public sealed class IdentityRedirectManagerTests
{
    private const string ADMIN_HOST = "admin.example.com";
    private const string ADMIN_BASE_URL = $"https://{ADMIN_HOST}";

    [Theory]
    [InlineData("")]
    [InlineData("Account/Manage")]
    [InlineData("/workflows")]
    public void AReturnAddressOnTheAdminApp_IsFollowed(string returnUrl)
    {
        NavigationException redirect = Assert.Throws<NavigationException>(() => RedirectTo(returnUrl));

        Assert.Equal(ADMIN_HOST, new Uri(redirect.Location).Host);
    }

    [Theory]
    [InlineData("//attacker.example/")]
    [InlineData("https://attacker.example/")]
    [InlineData("/\\attacker.example/")]
    [InlineData("\\\\attacker.example/")]
    [InlineData("/%2F/attacker.example/")]
    public void AReturnAddressOnAnotherSite_NeverSendsTheBrowserThere(string returnUrl)
    {
        Exception? ended = Record.Exception(() => RedirectTo(returnUrl));

        if (ended is NavigationException redirect)
        {
            Assert.True(new Uri(redirect.Location).Host == ADMIN_HOST, $"The browser was sent to {redirect.Location}.");
        }
    }

    private static void RedirectTo(string returnUrl)
    {
        new IdentityRedirectManager(new StaticRenderNavigationManager(ADMIN_BASE_URL), new AdminCookies(httpsOnly: true)).RedirectTo(returnUrl);
    }
}
