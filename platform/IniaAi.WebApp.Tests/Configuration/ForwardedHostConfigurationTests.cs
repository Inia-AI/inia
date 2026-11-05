using IniaAi.Common.Constants;
using IniaAi.Configuration.Security;
using IniaAi.WebApp.Configuration;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Net;
using System.Web;

namespace IniaAi.WebApp.Tests.Configuration;

public class ForwardedHostConfigurationTests
{
    private const string FORWARDED_HEADERS_ENABLED_KEY = "ForwardedHeaders_Enabled";
    private const string FORWARDED_HEADERS_ENABLED_ARG = $"--{FORWARDED_HEADERS_ENABLED_KEY}=true";
    private const string ALLOWED_HOSTS_KEY = $"{ForwardedHostSettings.SectionName}:{nameof(ForwardedHostSettings.AllowedHosts)}";
    private const string PROXY_HOST = "admin.teleport.example.com";
    private const string UPSTREAM_HOST = "localhost:5118";
    private const string LOAD_BALANCER_HOST = "admin.example.com";
    private const string CLIENT_ADDRESS = "192.0.2.10";
    private const string OIDC_ISSUER = "https://dev-123456.okta.com/oauth2/default";
    private const string OIDC_CLIENT_ID = "0oa1bcdef";
    private const string OIDC_AUTHORIZE_ENDPOINT = OIDC_ISSUER + "/v1/authorize";
    private const string CHALLENGE_PATH = "/challenge";
    private const string SIGNIN_CALLBACK_PATH = "/signin-oidc";

    private static WebApplicationBuilder CreateAdminBuilder(bool forwardedHeadersEnabled, string[] allowedHosts)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = forwardedHeadersEnabled ? [FORWARDED_HEADERS_ENABLED_ARG] : [],
            EnvironmentName = Environments.Production,
        });
        _ = builder.WebHost.UseTestServer();

        Dictionary<string, string?> settings = new()
        {
            ["Authentication:Oidc:Enabled"] = "true",
            ["Authentication:Oidc:Issuer"] = OIDC_ISSUER,
            ["Authentication:Oidc:ClientId"] = OIDC_CLIENT_ID,
        };
        for (int index = 0; index < allowedHosts.Length; index++)
        {
            settings[$"{ALLOWED_HOSTS_KEY}:{index}"] = allowedHosts[index];
        }
        _ = builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private static async Task<WebApplication> StartAdminAsync(params string[] allowedHosts)
    {
        WebApplicationBuilder builder = CreateAdminBuilder(forwardedHeadersEnabled: true, allowedHosts);
        _ = builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        _ = builder.Services.AddWebAppAuthentication(builder.Configuration);
        _ = builder.Services.AddForwardedHosts(builder.Configuration);

        // The identity provider is an external boundary: a static discovery document stands in
        // for the network fetch. It advertises no pushed-authorization endpoint, so the challenge
        // puts redirect_uri in the Location it returns.
        _ = builder.Services.Configure<OpenIdConnectOptions>(ExternalLoginProviders.OIDC, options =>
            options.Configuration = new OpenIdConnectConfiguration
            {
                Issuer = OIDC_ISSUER,
                AuthorizationEndpoint = OIDC_AUTHORIZE_ENDPOINT,
            });

        WebApplication app = builder.Build();
        _ = app.MapGet(CHALLENGE_PATH, () => Results.Challenge(authenticationSchemes: [ExternalLoginProviders.OIDC]));
        await app.StartAsync();
        return app;
    }

    private static async Task<string?> ChallengeRedirectUriAsync(WebApplication app, string host, string? forwardedHost)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, CHALLENGE_PATH);
        request.Headers.Host = host;
        request.Headers.Add(ForwardedHeadersDefaults.XForwardedForHeaderName, CLIENT_ADDRESS);
        request.Headers.Add(ForwardedHeadersDefaults.XForwardedProtoHeaderName, Uri.UriSchemeHttps);
        if (forwardedHost is not null)
        {
            request.Headers.Add(ForwardedHeadersDefaults.XForwardedHostHeaderName, forwardedHost);
        }

        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Uri location = response.Headers.Location!;
        Assert.Equal(OIDC_AUTHORIZE_ENDPOINT, location.GetLeftPart(UriPartial.Path));
        return HttpUtility.ParseQueryString(location.Query)[OpenIdConnectParameterNames.RedirectUri];
    }

    [Fact]
    public async Task ForwardedHostInAllowedList_BecomesTheSignInReturnHostAsync()
    {
        await using WebApplication app = await StartAdminAsync(PROXY_HOST);

        Assert.Equal($"https://{PROXY_HOST}{SIGNIN_CALLBACK_PATH}", await ChallengeRedirectUriAsync(app, UPSTREAM_HOST, PROXY_HOST));
    }

    [Fact]
    public async Task ForwardedHostOutsideAllowedList_IsIgnoredAsync()
    {
        await using WebApplication app = await StartAdminAsync(PROXY_HOST);

        Assert.Equal($"https://{UPSTREAM_HOST}{SIGNIN_CALLBACK_PATH}", await ChallengeRedirectUriAsync(app, UPSTREAM_HOST, "attacker.example"));
    }

    [Fact]
    public async Task NoAllowedHosts_IgnoresTheForwardedHostAsync()
    {
        await using WebApplication app = await StartAdminAsync();

        Assert.Equal($"https://{UPSTREAM_HOST}{SIGNIN_CALLBACK_PATH}", await ChallengeRedirectUriAsync(app, UPSTREAM_HOST, PROXY_HOST));
    }

    [Fact]
    public async Task RequestWithoutForwardedHost_KeepsItsOwnHostAsync()
    {
        await using WebApplication app = await StartAdminAsync(PROXY_HOST);

        Assert.Equal($"https://{LOAD_BALANCER_HOST}{SIGNIN_CALLBACK_PATH}", await ChallengeRedirectUriAsync(app, LOAD_BALANCER_HOST, forwardedHost: null));
    }

    [Fact]
    public async Task ForwardedHostWithPort_KeepsThePortAsync()
    {
        await using WebApplication app = await StartAdminAsync(PROXY_HOST);

        Assert.Equal($"https://{PROXY_HOST}:3080{SIGNIN_CALLBACK_PATH}", await ChallengeRedirectUriAsync(app, UPSTREAM_HOST, $"{PROXY_HOST}:3080"));
    }

    [Theory]
    [InlineData("attacker.example, " + PROXY_HOST, PROXY_HOST)]
    [InlineData(PROXY_HOST + ", attacker.example", UPSTREAM_HOST)]
    public async Task ForwardedHostChain_OnlyTheNearestProxyCountsAsync(string forwardedHost, string expectedHost)
    {
        await using WebApplication app = await StartAdminAsync(PROXY_HOST);

        Assert.Equal($"https://{expectedHost}{SIGNIN_CALLBACK_PATH}", await ChallengeRedirectUriAsync(app, UPSTREAM_HOST, forwardedHost));
    }

    [Fact]
    public void AllowedHostsWithoutForwardedHeadersEnabled_AreRejected()
    {
        WebApplicationBuilder builder = CreateAdminBuilder(forwardedHeadersEnabled: false, [PROXY_HOST]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.Services.AddForwardedHosts(builder.Configuration));
        Assert.Contains("ASPNETCORE_FORWARDEDHEADERS_ENABLED", exception.Message);
    }

    [Fact]
    public void AllowedHostsWithForwardedHeadersEnabledPaddedWithWhitespace_AreRejected()
    {
        WebApplicationBuilder builder = CreateAdminBuilder(forwardedHeadersEnabled: false, [PROXY_HOST]);
        _ = builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [FORWARDED_HEADERS_ENABLED_KEY] = "true " });

        _ = Assert.Throws<InvalidOperationException>(() => builder.Services.AddForwardedHosts(builder.Configuration));
    }

    [Fact]
    public void NoAllowedHostsWithoutForwardedHeadersEnabled_AreAccepted()
    {
        WebApplicationBuilder builder = CreateAdminBuilder(forwardedHeadersEnabled: false, []);

        Assert.Null(Record.Exception(() => builder.Services.AddForwardedHosts(builder.Configuration)));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("[::]")]
    [InlineData("0.0.0.0")]
    [InlineData(PROXY_HOST + ":3080")]
    [InlineData(" " + PROXY_HOST)]
    [InlineData("")]
    public void AllowedHostThatIsNotOneHostName_IsRejected(string allowedHost)
    {
        WebApplicationBuilder builder = CreateAdminBuilder(forwardedHeadersEnabled: true, [PROXY_HOST, allowedHost]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.Services.AddForwardedHosts(builder.Configuration));
        Assert.Contains($"'{allowedHost}'", exception.Message);
    }

    [Fact]
    public void AllowedHostsSetAsOneValueInsteadOfAList_AreRejected()
    {
        WebApplicationBuilder builder = CreateAdminBuilder(forwardedHeadersEnabled: true, []);
        _ = builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [ALLOWED_HOSTS_KEY] = PROXY_HOST });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.Services.AddForwardedHosts(builder.Configuration));
        Assert.Contains("__0", exception.Message);
    }
}
