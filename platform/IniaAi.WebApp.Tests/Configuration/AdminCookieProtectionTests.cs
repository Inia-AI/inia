using IniaAi.WebApp.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using IniaAi.Common.Constants;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApp.Tests.Configuration;

/// <summary>
/// A deployment that serves the admin app only over HTTPS turns on <see cref="AdminCookieConfiguration.HTTPS_ONLY_KEY"/>:
/// then every cookie of the admin app is <c>Secure</c> and carries a <c>__Host-</c> or <c>__Secure-</c> name, so
/// nothing on plain HTTP or on another host can set or read it. Off, the default, the open build keeps working on
/// <c>http://localhost</c>, where a browser refuses a <c>__Host-</c> cookie.
/// </summary>
public class AdminCookieProtectionTests(AdminCookieProtectionTests.HttpsOnlyHost httpsOnly) : IClassFixture<AdminCookieProtectionTests.HttpsOnlyHost>
{
    private const string TOKEN_PATH = "/antiforgery-token";
    private const string ANOTHER_SCHEME = "Another";

    [Fact]
    public async Task HttpsOnly_TheAntiforgeryCookieIsAHostCookie()
    {
        string cookie = await AntiforgeryCookieAsync(httpsOnly.App, "https://admin.example.com");

        Assert.StartsWith("__Host-Antiforgery=", cookie, StringComparison.Ordinal);
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Identity.Application")]
    [InlineData("Identity.External")]
    [InlineData("Identity.TwoFactorUserId")]
    [InlineData("Identity.TwoFactorRememberMe")]
    [InlineData(ANOTHER_SCHEME)]
    public void HttpsOnly_EverySignInCookieIsASecureHostCookie(string scheme)
    {
        CookieAuthenticationOptions options = httpsOnly.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);

        Assert.Equal($"__Host-{scheme}", options.Cookie.Name);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [Fact]
    public void HttpsOnly_TheGoogleSignInCookiesAreSecureCookies()
    {
        GoogleOptions options = httpsOnly.App.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get("Google");

        Assert.StartsWith("__Secure-", options.CorrelationCookie.Name, StringComparison.Ordinal);
        Assert.Equal(CookieSecurePolicy.Always, options.CorrelationCookie.SecurePolicy);
    }

    [Fact]
    public void HttpsOnly_TheOidcSignInCookiesAreSecureCookies()
    {
        OpenIdConnectOptions options = httpsOnly.App.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(ExternalLoginProviders.OIDC);

        Assert.StartsWith("__Secure-", options.CorrelationCookie.Name, StringComparison.Ordinal);
        Assert.Equal(CookieSecurePolicy.Always, options.CorrelationCookie.SecurePolicy);
        Assert.StartsWith("__Secure-", options.NonceCookie.Name, StringComparison.Ordinal);
        Assert.Equal(CookieSecurePolicy.Always, options.NonceCookie.SecurePolicy);
    }

    [Fact]
    public void HttpsOnly_TheStatusMessageCookieIsAHostCookie()
    {
        AdminCookies cookies = httpsOnly.App.Services.GetRequiredService<AdminCookies>();

        Assert.Equal("__Host-Identity.StatusMessage", cookies.StatusMessage);
        Assert.Equal(CookieSecurePolicy.Always, cookies.StatusMessageCookie.SecurePolicy);
    }

    [Fact]
    public async Task ByDefault_PlainHttpStillGetsAnAntiforgeryCookie()
    {
        await using WebApplication app = await StartAsync([]);

        string cookie = await AntiforgeryCookieAsync(app, "http://localhost");

        Assert.DoesNotContain("__Host-", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Identity.StatusMessage", app.Services.GetRequiredService<AdminCookies>().StatusMessage);
        Assert.Equal(
            ".AspNetCore.Identity.Application",
            app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).Cookie.Name);
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        _ = builder.WebHost.UseTestServer();
        _ = builder.Configuration.AddInMemoryCollection(settings);
        _ = builder.Services.AddWebAppAuthentication(builder.Configuration);
        _ = builder.Services.AddAuthentication().AddCookie(ANOTHER_SCHEME);
        _ = builder.Services.AddAntiforgery();
        _ = builder.Services.AddAdminCookieProtection(builder.Configuration);

        WebApplication app = builder.Build();
        _ = app.MapGet(TOKEN_PATH, (HttpContext context, IAntiforgery antiforgery) =>
        {
            _ = antiforgery.GetAndStoreTokens(context);
            return Results.Ok();
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<string> AntiforgeryCookieAsync(WebApplication app, string origin)
    {
        HttpClient client = app.GetTestClient();
        client.BaseAddress = new Uri(origin);

        using HttpResponseMessage response = await client.GetAsync(TOKEN_PATH, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"The token endpoint answered {response.StatusCode}");
        return Assert.Single(response.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("Antiforgery", StringComparison.Ordinal));
    }

    /// <summary>One admin app host with HTTPS-only cookies and Google and OIDC sign-in on, shared by the tests that only read it.</summary>
    public sealed class HttpsOnlyHost : IAsyncLifetime
    {
        public WebApplication App { get; private set; } = null!;

        public async ValueTask InitializeAsync()
        {
            App = await StartAsync(new()
            {
                [AdminCookieConfiguration.HTTPS_ONLY_KEY] = "true",
                ["Authentication:Google:Enabled"] = "true",
                ["Authentication:Google:ClientId"] = "client",
                ["Authentication:Google:ClientSecret"] = "secret",
                ["Authentication:Oidc:Enabled"] = "true",
                ["Authentication:Oidc:Issuer"] = "https://login.example.com/tenant/v2.0",
                ["Authentication:Oidc:ClientId"] = "client",
            });
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
        }
    }
}
