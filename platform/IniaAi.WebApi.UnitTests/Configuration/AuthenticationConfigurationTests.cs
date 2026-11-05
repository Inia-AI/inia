using IniaAi.Configuration.Security;
using IniaAi.WebApi.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// The web app runs in the browser, so it cannot keep a client secret, and it signs in without
/// naming a client. The token endpoint must keep accepting such requests.
/// </summary>
public class AuthenticationConfigurationTests
{
    private static OpenIddictServerOptions ServerOptions()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{JwtSettings.SectionName}:{nameof(JwtSettings.SecretKey)}"] = new string('k', 64),
                [$"{JwtSettings.SectionName}:{nameof(JwtSettings.Issuer)}"] = "https://api.example.com",
                [$"{JwtSettings.SectionName}:{nameof(JwtSettings.TokenValidityInMinutes)}"] = "30",
            })
            .Build();

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
}
