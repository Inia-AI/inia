using IniaAi.WebApp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IniaAi.WebApp.Tests.Configuration;

/// <summary>
/// Every admin app page carries a policy that runs only the app's own scripts, embeds no plugin and sends forms
/// only to the app and to a sign-in provider the deployment turned on. Every answer carries nosniff, a referrer
/// policy and a same-origin frame rule, and an answer that sets its own policy or frame rule keeps it.
/// </summary>
public class AdminSecurityHeadersTests(AdminSecurityHeadersTests.DefaultHost host) : IClassFixture<AdminSecurityHeadersTests.DefaultHost>
{
    private const string PAGE = "/page";
    private const string DATA = "/data";
    private const string OWN_POLICY_PAGE = "/own-policy";
    private const string OWN_POLICY = "sandbox";
    private const string OWN_FRAME_RULE = "DENY";

    [Fact]
    public async Task APage_RunsOnlyTheAppsOwnScripts_AndEmbedsNoPlugin()
    {
        HttpResponseMessage page = await host.App.GetTestClient().GetAsync(PAGE, TestContext.Current.CancellationToken);

        Dictionary<string, string> policy = Directives(page);
        Assert.Equal("'self'", policy["script-src"]);
        Assert.Equal("'none'", policy["object-src"]);
        Assert.Equal("'self'", policy["base-uri"]);
        Assert.Equal("'self'", policy["frame-ancestors"]);
        Assert.Equal("'self'", policy["form-action"]);
        AssertAnswerHeaders(page);
    }

    [Fact]
    public async Task WithGoogleAndOidcSignIn_FormsMayGoToTheirSignInPages()
    {
        await using WebApplication app = await StartAsync(new()
        {
            ["Authentication:Google:Enabled"] = "true",
            ["Authentication:Google:ClientId"] = "client",
            ["Authentication:Google:ClientSecret"] = "secret",
            ["Authentication:Oidc:Enabled"] = "true",
            ["Authentication:Oidc:Issuer"] = "https://login.example.com/tenant/v2.0",
            ["Authentication:Oidc:ClientId"] = "client",
        });

        HttpResponseMessage page = await app.GetTestClient().GetAsync(PAGE, TestContext.Current.CancellationToken);

        Assert.Equal("'self' https://accounts.google.com https://login.example.com", Directives(page)["form-action"]);
    }

    [Fact]
    public async Task AnAnswerThatIsNotAPage_CarriesNoPolicy_ButTheOtherHeaders()
    {
        HttpResponseMessage data = await host.App.GetTestClient().GetAsync(DATA, TestContext.Current.CancellationToken);

        Assert.False(data.Headers.Contains("Content-Security-Policy"));
        AssertAnswerHeaders(data);
    }

    [Fact]
    public async Task APageThatSetsItsOwnPolicyAndFrameRule_KeepsThem()
    {
        HttpResponseMessage page = await host.App.GetTestClient().GetAsync(OWN_POLICY_PAGE, TestContext.Current.CancellationToken);

        Assert.Equal(OWN_POLICY, Assert.Single(page.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal(OWN_FRAME_RULE, Assert.Single(page.Headers.GetValues("X-Frame-Options")));
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        _ = builder.WebHost.UseTestServer();
        _ = builder.Configuration.AddInMemoryCollection(settings);

        WebApplication app = builder.Build();
        _ = app.UseAdminSecurityHeaders();
        _ = app.MapGet(PAGE, () => Results.Content("<html></html>", "text/html"));
        _ = app.MapGet(DATA, () => Results.Json(new { ok = true }));
        _ = app.MapGet(OWN_POLICY_PAGE, (HttpContext context) =>
        {
            context.Response.Headers.ContentSecurityPolicy = OWN_POLICY;
            context.Response.Headers.XFrameOptions = OWN_FRAME_RULE;
            return Results.Content("<html></html>", "text/html");
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static Dictionary<string, string> Directives(HttpResponseMessage response)
    {
        string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        return policy
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directive => directive.Split(' ', 2))
            .ToDictionary(parts => parts[0], parts => parts[1]);
    }

    private static void AssertAnswerHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("SAMEORIGIN", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
    }

    /// <summary>One admin app host with the default settings, shared by the tests that only read it.</summary>
    public sealed class DefaultHost : IAsyncLifetime
    {
        public WebApplication App { get; private set; } = null!;

        public async ValueTask InitializeAsync()
        {
            App = await StartAsync([]);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
        }
    }
}
