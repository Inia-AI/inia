using System.Net;
using IniaAi.Configuration.Security;
using IniaAi.WebApp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IniaAi.WebApp.Tests.Configuration;

/// <summary>
/// The admin app's sign-in limit and audit read the client's address. Once a deployment lists its
/// proxies, a forwarded address counts only from them, as in the API. A deployment that lists none
/// keeps the one trusted hop it had, because its sign-in redirect depends on the forwarded scheme.
/// </summary>
public class AdminTrustedProxyTests
{
    private const string PROXY = "10.0.1.20";
    private const string STRANGER = "198.51.100.9";
    private const string CLIENT = "203.0.113.7";
    private const string PROBE = "/client-address";

    [Fact]
    public async Task AForwardedAddressFromAListedProxy_IsTheClientAddress()
    {
        await using WebApplication app = await StartAsync(new() { [$"{TrustedProxies.KNOWN_NETWORKS_KEY}:0"] = "10.0.0.0/16" });

        Assert.Equal(CLIENT, await ClientAddressSeenAsync(app, PROXY));
    }

    [Fact]
    public async Task AForwardedAddressFromAnyOtherSender_IsIgnored()
    {
        await using WebApplication app = await StartAsync(new() { [$"{TrustedProxies.KNOWN_NETWORKS_KEY}:0"] = "10.0.0.0/16" });

        Assert.Equal(STRANGER, await ClientAddressSeenAsync(app, STRANGER));
    }

    [Fact]
    public async Task WithNoListedProxy_TheOneForwardedHopStillCounts()
    {
        await using WebApplication app = await StartAsync([]);

        Assert.Equal(CLIENT, await ClientAddressSeenAsync(app, STRANGER));
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = ["--ForwardedHeaders_Enabled=true"],
            EnvironmentName = Environments.Production,
        });
        _ = builder.WebHost.UseTestServer();
        _ = builder.Configuration.AddInMemoryCollection(settings);
        _ = builder.Services.AddTrustedProxies(builder.Configuration);

        WebApplication app = builder.Build();
        _ = app.MapGet(PROBE, (HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<string> ClientAddressSeenAsync(WebApplication app, string peer)
    {
        HttpContext context = await app.GetTestServer().SendAsync(request =>
        {
            request.Request.Path = PROBE;
            request.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            request.Request.Headers[ForwardedHeadersDefaults.XForwardedForHeaderName] = CLIENT;
        });

        using StreamReader reader = new(context.Response.Body);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }
}
