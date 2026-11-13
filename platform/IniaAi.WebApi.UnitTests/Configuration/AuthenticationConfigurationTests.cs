using IniaAi.Configuration.Security;
using IniaAi.WebApi.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// The web app runs in the browser, so it cannot keep a client secret, and it signs in without
/// naming a client. The token endpoint must keep accepting such requests. The service account's
/// grant can be turned off, for a stack whose integrations do not use it.
/// </summary>
public class AuthenticationConfigurationTests
{
    private static OpenIddictServerOptions ServerOptions(bool? serviceAccountGrant = null)
    {
        Dictionary<string, string?> settings = new()
        {
            [$"{JwtSettings.SectionName}:{nameof(JwtSettings.SecretKey)}"] = new string('k', 64),
            [$"{JwtSettings.SectionName}:{nameof(JwtSettings.Issuer)}"] = "https://api.example.com",
            [$"{JwtSettings.SectionName}:{nameof(JwtSettings.TokenValidityInMinutes)}"] = "30",
        };
        if (serviceAccountGrant is { } enabled)
        {
            settings[$"{ServiceAccountAuthSettings.SectionName}:{nameof(ServiceAccountAuthSettings.Enabled)}"] = enabled.ToString();
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = AuthenticationConfiguration.AddAuthentication(services, configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value;
    }

    [Fact]
    public void TheTokenEndpointAcceptsARequestThatNamesNoClient()
    {
        OpenIddictServerOptions options = ServerOptions();

        Assert.True(options.AcceptAnonymousClients);
    }

    [Fact]
    public void TheServiceAccountGrant_IsOnByDefault()
    {
        Assert.Contains(GrantTypes.ClientCredentials, ServerOptions().GrantTypes);
    }

    [Fact]
    public void TheServiceAccountGrant_TurnedOff_IsNotAGrantTheTokenEndpointKnows()
    {
        OpenIddictServerOptions options = ServerOptions(serviceAccountGrant: false);

        Assert.DoesNotContain(GrantTypes.ClientCredentials, options.GrantTypes);
        Assert.Contains(GrantTypes.RefreshToken, options.GrantTypes);
    }
}
