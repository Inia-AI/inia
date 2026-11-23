using IniaAi.WebApi.UnitTests.Common;
using System.Net;
using System.Reflection;
using IniaAi.Testing;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Controllers.v1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// What a browser or a proxy in front of the API meets before any controller runs: which origins may
/// call it, which address counts as the client's, and when sign-in traffic is turned away.
/// </summary>
public class ClientEdgeTests
{
    private const string PEER_HEADER = "X-Test-Peer";
    private const string PROXY = "10.0.1.20";
    private const string CLIENT = "203.0.113.7";
    private const string LISTED_ORIGIN = "https://app.example.com";

    public static TheoryData<string, string> SignInEndpoints => new()
    {
        { nameof(AuthenticationController), nameof(AuthenticationController.Exchange) },
        { nameof(AuthenticationController), nameof(AuthenticationController.VerifyMfa) },
        { nameof(AuthenticationController), nameof(AuthenticationController.SendEmailOtp) },
        { nameof(PasswordsController), nameof(PasswordsController.ForgotPasswordAsync) },
        { nameof(PasswordsController), nameof(PasswordsController.ResetPasswordAsync) },
    };

    [Theory]
    [MemberData(nameof(SignInEndpoints))]
    public void EachSignInEndpoint_IsLimited(string controller, string action)
    {
        Type type = typeof(AuthenticationController).Assembly.GetTypes().Single(t => t.Name == controller);
        MethodInfo method = type.GetMethod(action)!;

        EnableRateLimitingAttribute? limit = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.Equal(RateLimitingConfiguration.SIGN_IN_POLICY, limit?.PolicyName);
    }

    [Fact]
    public async Task TheFirstRequestPastTheLimit_Gets429()
    {
        await using WebApplication app = await StartAsync(new() { ["RateLimiting:SignIn:PermitLimit"] = "2" });
        HttpClient client = app.GetTestClient();

        HttpStatusCode[] statuses = [.. await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PostFromAsync(client, CLIENT)))];

        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task ARequestRefusedForTheLimit_LeavesAWarningNamingThePolicyAndTheClient()
    {
        var logs = new CapturingLoggerProvider();
        await using WebApplication app = await StartAsync(new() { ["RateLimiting:SignIn:PermitLimit"] = "1" }, logs);
        HttpClient client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, CLIENT));
        Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostFromAsync(client, CLIENT));

        CapturedLogEntry refusal = Assert.Single(logs.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.StartsWith("IniaAi.", refusal.Category, StringComparison.Ordinal);
        Assert.Contains(RateLimitingConfiguration.SIGN_IN_POLICY, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(CLIENT, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLimit_CountsEachClientSeparately()
    {
        await using WebApplication app = await StartAsync(new() { ["RateLimiting:SignIn:PermitLimit"] = "1" });
        HttpClient client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, CLIENT));
        Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, "198.51.100.9"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostFromAsync(client, CLIENT));
    }

    [Fact]
    public async Task AnotherSpellingOfTheSamePath_SharesItsLimit()
    {
        await using WebApplication app = await StartAsync(new() { ["RateLimiting:SignIn:PermitLimit"] = "1" });
        HttpClient client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, CLIENT));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostFromAsync(client, CLIENT, "edge-probe/"));
    }

    [Fact]
    public async Task IPv6ClientsInOneNetwork_ShareALimit()
    {
        await using WebApplication app = await StartAsync(new() { ["RateLimiting:SignIn:PermitLimit"] = "1" });
        HttpClient client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, "2001:db8:0:1::1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostFromAsync(client, "2001:db8:0:1::2"));
        Assert.Equal(HttpStatusCode.OK, await PostFromAsync(client, "2001:db8:0:2::1"));
    }

    [Theory]
    [InlineData("RateLimiting:SignIn:PermitLimit")]
    [InlineData("RateLimiting:SignIn:WindowSeconds")]
    public async Task ALimitOfZero_StopsStartupNamingTheKey(string key)
    {
        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(new() { [key] = "0" }));

        Assert.Contains(key, refusal.Message);
    }

    [Fact]
    public async Task AForwardedClientAddressFromAConfiguredProxy_IsTheClientAddress()
    {
        await using WebApplication app = await StartAsync(new() { ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/16" });

        string seen = await ClientAddressSeenAsync(app.GetTestClient(), PROXY, $"198.51.100.250, {CLIENT}, 10.0.2.30");

        Assert.Equal(CLIENT, seen);
    }

    [Fact]
    public async Task AForwardedClientAddressFromAnyOtherSender_IsIgnored()
    {
        await using WebApplication app = await StartAsync(new() { ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/16" });

        string seen = await ClientAddressSeenAsync(app.GetTestClient(), CLIENT, "198.51.100.250");

        Assert.Equal(CLIENT, seen);
    }

    [Fact]
    public async Task APreflightFromAListedOrigin_IsAllowed()
    {
        await using WebApplication app = await StartAsync(new() { ["Cors:AllowedOrigins:0"] = LISTED_ORIGIN });

        HttpResponseMessage response = await PreflightAsync(app.GetTestClient(), LISTED_ORIGIN);

        Assert.Equal(LISTED_ORIGIN, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task APreflightFromAnUnlistedOrigin_GetsNoAllowOrigin()
    {
        await using WebApplication app = await StartAsync(new() { ["Cors:AllowedOrigins:0"] = LISTED_ORIGIN });

        HttpResponseMessage response = await PreflightAsync(app.GetTestClient(), "https://example.com");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task AnActualRequestFromAnUnlistedOrigin_GetsNoAllowOrigin()
    {
        await using WebApplication app = await StartAsync(new() { ["Cors:AllowedOrigins:0"] = LISTED_ORIGIN });
        HttpClient client = app.GetTestClient();

        using HttpResponseMessage listed = await PostWithOriginAsync(client, LISTED_ORIGIN);
        using HttpResponseMessage unlisted = await PostWithOriginAsync(client, "https://unlisted.example");

        Assert.Equal(LISTED_ORIGIN, Assert.Single(listed.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(HttpStatusCode.OK, unlisted.StatusCode);
        Assert.False(unlisted.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(unlisted.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task AnOriginThatOnlyStartsLikeAListedOne_GetsNoAllowOrigin()
    {
        await using WebApplication app = await StartAsync(new() { ["Cors:AllowedOrigins:0"] = LISTED_ORIGIN });
        HttpClient client = app.GetTestClient();

        using HttpResponseMessage listed = await PostWithOriginAsync(client, LISTED_ORIGIN);
        using HttpResponseMessage lookalike = await PostWithOriginAsync(client, $"{LISTED_ORIGIN}.evil.example");
        using HttpResponseMessage preflight = await PreflightAsync(client, $"{LISTED_ORIGIN}.evil.example");

        Assert.Equal(LISTED_ORIGIN, Assert.Single(listed.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(lookalike.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task AnEmptyOriginList_AllowsNoOrigin()
    {
        await using WebApplication app = await StartAsync([]);

        HttpResponseMessage response = await PreflightAsync(app.GetTestClient(), LISTED_ORIGIN);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task AWildcardOrigin_StopsStartupNamingTheKey()
    {
        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartAsync(new() { ["Cors:AllowedOrigins:0"] = LISTED_ORIGIN, ["Cors:AllowedOrigins:1"] = "*" }));

        Assert.Contains(CorsConfiguration.ALLOWED_ORIGINS_KEY, refusal.Message);
    }

    [Theory]
    [InlineData(CorsConfiguration.ALLOWED_ORIGINS_KEY, LISTED_ORIGIN)]
    [InlineData(ForwardedHeadersConfiguration.KNOWN_NETWORKS_KEY, "10.0.0.0/16")]
    public async Task AListSetAsOneValue_StopsStartupNamingTheKey(string key, string value)
    {
        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(new() { [key] = value }));

        Assert.Contains(key, refusal.Message);
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings, ILoggerProvider? logs = null)
    {
        WebApplicationBuilder builder = TestWebApplication.CreateBuilder();
        if (logs is not null)
        {
            _ = builder.Logging.AddProvider(logs);
        }

        builder.Configuration.Sources.Clear();
        _ = builder.Configuration.AddInMemoryCollection(settings);
        _ = builder.Services.AddControllers().AddApplicationPart(typeof(ClientEdgeTests).Assembly);
        _ = builder.Services.AddConfiguredApiVersioning();
        _ = builder.Services
            .AddClientForwardedHeaders(builder.Configuration)
            .AddCorsWithPolicy(builder.Configuration)
            .AddSignInRateLimiting(builder.Configuration);

        WebApplication app = builder.Build();
        _ = app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue(PEER_HEADER, out var peer))
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer!);
            }

            return next();
        });
        _ = app.UseForwardedHeaders();
        _ = app.UseRouting();
        _ = app.UseCors(CorsConfiguration.POLICY_NAME);
        _ = app.UseRateLimiter();
        _ = app.MapControllers();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<HttpStatusCode> PostFromAsync(HttpClient client, string peer, string path = "edge-probe")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(PEER_HEADER, peer);
        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task<string> ClientAddressSeenAsync(HttpClient client, string peer, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "edge-probe");
        request.Headers.Add(PEER_HEADER, peer);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostWithOriginAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "edge-probe");
        request.Headers.Add(PEER_HEADER, CLIENT);
        request.Headers.Add("Origin", origin);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PreflightAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "edge-probe");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}

[ApiController]
[Route("edge-probe")]
public class EdgeProbeController : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingConfiguration.SIGN_IN_POLICY)]
    public IActionResult Post()
    {
        return Content(HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
    }
}
