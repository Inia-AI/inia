using System.Net;
using System.Net.Http.Headers;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.UnitTests.Common;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// The browser protection headers reach every answer of the API, the ones a middleware writes by itself
/// included: a refusal, a rate-limit answer, a preflight, a failure and an unknown path.
/// </summary>
public class SecurityHeadersTests(SecurityHeadersTests.DefaultHost host) : IClassFixture<SecurityHeadersTests.DefaultHost>
{
    private const string LISTED_ORIGIN = "https://app.example.com";
    internal const string OWN_CACHE_RULE = "private, max-age=60";
    internal const string OWN_POLICY = "sandbox";

    [Theory]
    [InlineData("headers-probe/open", HttpStatusCode.OK)]
    [InlineData("headers-probe/signed-in", HttpStatusCode.Unauthorized)]
    [InlineData("headers-probe/missing", HttpStatusCode.NotFound)]
    [InlineData("headers-probe/fails", HttpStatusCode.InternalServerError)]
    public async Task AnAnswer_CarriesTheSecurityHeaders(string path, HttpStatusCode status)
    {
        using HttpResponseMessage response = await host.Client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        AssertProtected(response);
    }

    [Fact]
    public async Task ATooManyRequestsAnswer_CarriesTheSecurityHeaders()
    {
        await using WebApplication app = await StartAsync(new() { ["RateLimiting:SignIn:PermitLimit"] = "1" });
        HttpClient client = app.GetTestClient();

        using HttpResponseMessage first = await client.PostAsync("edge-probe", null, TestContext.Current.CancellationToken);
        using HttpResponseMessage second = await client.PostAsync("edge-probe", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        AssertProtected(second);
    }

    [Fact]
    public async Task APreflight_CarriesTheSecurityHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "headers-probe/open");
        request.Headers.Add("Origin", LISTED_ORIGIN);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(LISTED_ORIGIN, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        AssertProtected(response);
    }

    [Fact]
    public async Task AnApiAnswer_CarriesAPolicyThatLoadsNothing()
    {
        using HttpResponseMessage response = await host.Client.GetAsync("headers-probe/open", TestContext.Current.CancellationToken);

        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task TheApiDocumentationPage_KeepsItsOwnScripts()
    {
        await using WebApplication app = await StartAsync(servesApiDocumentation: true);

        using HttpResponseMessage page = await app.GetTestClient().GetAsync("swagger/index.html", TestContext.Current.CancellationToken);
        using HttpResponseMessage answer = await app.GetTestClient().GetAsync("headers-probe/open", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.False(page.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", Assert.Single(page.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(answer.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task AnApiAnswer_IsNotStoredByTheBrowser()
    {
        using HttpResponseMessage response = await host.Client.GetAsync("headers-probe/open", TestContext.Current.CancellationToken);

        Assert.Equal("no-store", Assert.Single(response.Headers.GetValues("Cache-Control")));
    }

    [Fact]
    public async Task AnAnswerThatSetsItsOwnCacheRule_KeepsIt()
    {
        using HttpResponseMessage response = await host.Client.GetAsync("headers-probe/own-cache", TestContext.Current.CancellationToken);

        CacheControlHeaderValue? rule = response.Headers.CacheControl;
        Assert.NotNull(rule);
        Assert.True(rule.Private);
        Assert.Equal(TimeSpan.FromSeconds(60), rule.MaxAge);
        Assert.False(rule.NoStore);
    }

    [Fact]
    public async Task AnAnswerThatSetsItsOwnPolicy_KeepsIt()
    {
        using HttpResponseMessage response = await host.Client.GetAsync("headers-probe/own-policy", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OWN_POLICY, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    /// <summary>The API's middleware order on one host, started once for the tests that only read from it.</summary>
    public sealed class DefaultHost : IAsyncLifetime
    {
        private WebApplication _app = null!;

        public HttpClient Client { get; private set; } = null!;

        public async ValueTask InitializeAsync()
        {
            _app = await StartAsync();
            Client = _app.GetTestClient();
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
        }
    }

    private static void AssertProtected(HttpResponseMessage response)
    {
        string status = $"{(int)response.StatusCode} {response.StatusCode}";
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out IEnumerable<string>? noSniff), $"{status} has no X-Content-Type-Options");
        Assert.Equal("nosniff", Assert.Single(noSniff));
        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out IEnumerable<string>? referrer), $"{status} has no Referrer-Policy");
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(referrer));
        Assert.True(response.Headers.TryGetValues("X-Frame-Options", out IEnumerable<string>? frames), $"{status} has no X-Frame-Options");
        Assert.Equal("DENY", Assert.Single(frames));
        Assert.True(response.Headers.Contains("Content-Security-Policy"), $"{status} has no Content-Security-Policy");
        Assert.False(response.Headers.Contains("X-XSS-Protection"), $"{status} turns on the legacy XSS filter");
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?>? settings = null, bool servesApiDocumentation = false)
    {
        WebApplicationBuilder builder = TestWebApplication.CreateBuilder();
        if (servesApiDocumentation)
        {
            builder.Environment.EnvironmentName = "LocalDevelopment";
        }

        builder.Configuration.Sources.Clear();
        _ = builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(settings ?? []) { ["Cors:AllowedOrigins:0"] = LISTED_ORIGIN });
        _ = builder.Services.AddControllers().AddApplicationPart(typeof(SecurityHeadersTests).Assembly);
        _ = builder.Services.AddConfiguredApiVersioning();
        _ = builder.Services.AddProblemDetails();
        _ = builder.Services
            .AddClientForwardedHeaders(builder.Configuration)
            .AddCorsWithPolicy(builder.Configuration)
            .AddSignInRateLimiting(builder.Configuration);
        _ = builder.Services.AddAuthentication(BearerTokenDefaults.AuthenticationScheme).AddBearerToken();
        _ = builder.Services.AddAuthorization();

        WebApplication app = builder.Build();
        _ = app.UseForwardedHeaders();
        _ = app.UseSecurityHeaders();
        _ = app.UseExceptionHandler();
        _ = app.UseRouting();
        _ = app.UseCors(CorsConfiguration.POLICY_NAME);
        _ = app.UseRateLimiter();
        _ = app.UseAuthentication();
        _ = app.UseAuthorization();
        _ = app.MapControllers();
        _ = app.MapGet("/swagger/index.html", () => Results.Content("<html></html>", "text/html"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}

[ApiController]
[Route("headers-probe")]
public class HeadersProbeController : ControllerBase
{
    [HttpGet("open")]
    [AllowAnonymous]
    public IActionResult Open()
    {
        return Content("open");
    }

    [HttpGet("signed-in")]
    [Authorize]
    public IActionResult SignedIn()
    {
        return Content("signed in");
    }

    [HttpGet("own-cache")]
    public IActionResult OwnCache()
    {
        Response.Headers.CacheControl = SecurityHeadersTests.OWN_CACHE_RULE;
        return Content("cached");
    }

    [HttpGet("own-policy")]
    public IActionResult OwnPolicy()
    {
        Response.Headers.ContentSecurityPolicy = SecurityHeadersTests.OWN_POLICY;
        return Content("preview");
    }

    [HttpGet("fails")]
    public IActionResult Fails()
    {
        throw new InvalidOperationException("probe failure");
    }
}
