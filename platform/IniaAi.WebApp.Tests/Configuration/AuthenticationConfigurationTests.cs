using IniaAi.Application.Common.Services;
using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApp.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace IniaAi.WebApp.Tests.Configuration;

public class AuthenticationConfigurationTests
{
    private const string OIDC_ISSUER = "https://dev-123456.okta.com/oauth2/default";
    private const string OIDC_CLIENT_ID = "0oa1bcdef";

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddDataProtection();
        _ = services.AddWebAppAuthentication(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<AuthenticationScheme?> GetSchemeAsync(ServiceProvider provider, string name)
    {
        IAuthenticationSchemeProvider schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        return await schemes.GetSchemeAsync(name);
    }

    [Fact]
    public async Task TheAdminApp_HashesPasswordsWithTheOwaspWorkFactor()
    {
        await using ServiceProvider provider = BuildProvider([]);
        IPasswordHasher<User> hasher = provider.GetRequiredService<IPasswordHasher<User>>();
        var user = new User();

        string hash = hasher.HashPassword(user, "Abcdefg1");

        Assert.Equal(PasswordHashing.ITERATIONS, provider.GetRequiredService<IOptions<PasswordHasherOptions>>().Value.IterationCount);
        Assert.Equal(PasswordVerificationResult.Success, PasswordHashing.Hasher.VerifyHashedPassword(user, hash, "Abcdefg1"));
    }

    [Fact]
    public async Task NoAuthConfig_RegistersNoExternalSchemesAsync()
    {
        await using ServiceProvider provider = BuildProvider([]);

        Assert.Null(await GetSchemeAsync(provider, ExternalLoginProviders.GOOGLE));
        Assert.Null(await GetSchemeAsync(provider, ExternalLoginProviders.OIDC));
        Assert.NotNull(await GetSchemeAsync(provider, IdentityConstants.ApplicationScheme));
    }

    [Fact]
    public async Task GoogleEnabledWithCredentials_RegistersGoogleSchemeAsync()
    {
        await using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Authentication:Google:Enabled"] = "true",
            ["Authentication:Google:ClientId"] = "google-client",
            ["Authentication:Google:ClientSecret"] = "google-secret",
        });

        AuthenticationScheme? scheme = await GetSchemeAsync(provider, ExternalLoginProviders.GOOGLE);
        Assert.NotNull(scheme);
        Assert.Equal(typeof(GoogleHandler), scheme!.HandlerType);

        GoogleOptions options = provider.GetRequiredService<IOptionsMonitor<GoogleOptions>>()
            .Get(ExternalLoginProviders.GOOGLE);
        Assert.Equal("google-client", options.ClientId);
        Assert.Equal(IdentityConstants.ExternalScheme, options.SignInScheme);
        Assert.Equal("/signin-google", options.CallbackPath.Value);
    }

    [Fact]
    public void TheGoogleScheme_KeepsTheWorkspaceDomain_ThatProvesAnAddress()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Authentication:Google:Enabled"] = "true",
            ["Authentication:Google:ClientId"] = "google-client",
            ["Authentication:Google:ClientSecret"] = "google-secret",
        });

        GoogleOptions options = provider.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(ExternalLoginProviders.GOOGLE);

        Assert.Contains(options.ClaimActions, action => action.ClaimType == GoogleAddressProof.HOSTED_DOMAIN_CLAIM);
    }

    [Theory]
    [InlineData("true", "google-client", "")]
    [InlineData("true", "", "google-secret")]
    [InlineData("false", "google-client", "google-secret")]
    public async Task GoogleMissingPrerequisites_DoesNotRegisterSchemeAsync(string enabled, string clientId, string clientSecret)
    {
        await using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Authentication:Google:Enabled"] = enabled,
            ["Authentication:Google:ClientId"] = clientId,
            ["Authentication:Google:ClientSecret"] = clientSecret,
        });

        Assert.Null(await GetSchemeAsync(provider, ExternalLoginProviders.GOOGLE));
    }

    [Fact]
    public async Task OidcEnabled_RegistersPublicPkceClientWithDisplayNameAsync()
    {
        await using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Authentication:Oidc:Enabled"] = "true",
            ["Authentication:Oidc:Issuer"] = OIDC_ISSUER,
            ["Authentication:Oidc:ClientId"] = OIDC_CLIENT_ID,
            ["Authentication:Oidc:DisplayName"] = "Okta",
        });

        AuthenticationScheme? scheme = await GetSchemeAsync(provider, ExternalLoginProviders.OIDC);
        Assert.NotNull(scheme);
        Assert.Equal(typeof(OpenIdConnectHandler), scheme!.HandlerType);
        Assert.Equal("Okta", scheme.DisplayName);

        OpenIdConnectOptions options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(ExternalLoginProviders.OIDC);
        Assert.Equal(OIDC_ISSUER, options.Authority);
        Assert.Equal(OIDC_CLIENT_ID, options.ClientId);
        Assert.Null(options.ClientSecret);
        Assert.Equal(OpenIdConnectResponseType.Code, options.ResponseType);
        Assert.True(options.UsePkce);
        Assert.True(options.GetClaimsFromUserInfoEndpoint);
        Assert.Contains(OpenIdConnectScope.Email, options.Scope);
        Assert.Equal(IdentityConstants.ExternalScheme, options.SignInScheme);
        Assert.Equal("/signin-oidc", options.CallbackPath.Value);
    }

    [Fact]
    public async Task OidcWithClientSecret_ConfiguresConfidentialClientAsync()
    {
        await using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Authentication:Oidc:Enabled"] = "true",
            ["Authentication:Oidc:Issuer"] = OIDC_ISSUER,
            ["Authentication:Oidc:ClientId"] = OIDC_CLIENT_ID,
            ["Authentication:Oidc:ClientSecret"] = "confidential-secret",
        });

        OpenIdConnectOptions options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(ExternalLoginProviders.OIDC);
        Assert.Equal("confidential-secret", options.ClientSecret);
        Assert.True(options.UsePkce);
    }

    [Theory]
    [InlineData("true", "", OIDC_CLIENT_ID)]
    [InlineData("true", OIDC_ISSUER, "")]
    [InlineData("false", OIDC_ISSUER, OIDC_CLIENT_ID)]
    public async Task OidcMissingPrerequisites_DoesNotRegisterSchemeAsync(string enabled, string issuer, string clientId)
    {
        await using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Authentication:Oidc:Enabled"] = enabled,
            ["Authentication:Oidc:Issuer"] = issuer,
            ["Authentication:Oidc:ClientId"] = clientId,
        });

        Assert.Null(await GetSchemeAsync(provider, ExternalLoginProviders.OIDC));
    }

    [Fact]
    public void ThePasswordRule_IsTheSameAsTheApis()
    {
        using ServiceProvider provider = BuildProvider([]);
        PasswordOptions expected = new();
        PasswordPolicy.Apply(expected);

        PasswordOptions actual = provider.GetRequiredService<IOptions<IdentityOptions>>().Value.Password;

        Assert.Equivalent(expected, actual);
        Assert.False(actual.RequireNonAlphanumeric);
    }

    [Fact]
    public void PasswordSettings_BindLockoutOptions()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Authentication:Password:MaxFailedAccessAttempts"] = "3",
            ["Authentication:Password:LockoutDurationMinutes"] = "42",
        });

        IdentityOptions identityOptions = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.Equal(3, identityOptions.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(42), identityOptions.Lockout.DefaultLockoutTimeSpan);
        Assert.True(identityOptions.Lockout.AllowedForNewUsers);
        Assert.True(identityOptions.SignIn.RequireConfirmedAccount);
    }
}
