using IniaAi.Configuration.Security;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.UnitTests.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace IniaAi.WebApi.UnitTests.Configuration;

/// <summary>
/// Google sign-in checks that a token was issued to this deployment's client id, so the API refuses
/// to start with Google sign-in on and no client id, instead of starting with a sign-in that can
/// never succeed.
/// </summary>
public class GoogleSignInSettingsTests
{
    private const string ENABLED_KEY = $"{GoogleAuthSettings.SectionName}:{nameof(GoogleAuthSettings.Enabled)}";

    [Fact]
    public async Task GoogleSignInWithItsClientId_Starts()
    {
        await using WebApplication app = await StartAsync(new() { [ENABLED_KEY] = "true", [GoogleAuthSettings.CLIENT_ID_KEY] = "client.apps.example" });
    }

    [Fact]
    public async Task GoogleSignInTurnedOff_StartsWithoutAClientId()
    {
        await using WebApplication app = await StartAsync(new() { [ENABLED_KEY] = "false" });
    }

    [Fact]
    public async Task GoogleSignInWithoutAClientId_StopsStartupNamingTheKey()
    {
        Exception refusal = await Assert.ThrowsAnyAsync<Exception>(() => StartAsync(new() { [ENABLED_KEY] = "true", [GoogleAuthSettings.CLIENT_ID_KEY] = " " }));

        Assert.Contains(GoogleAuthSettings.CLIENT_ID_KEY, refusal.Message);
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        WebApplicationBuilder builder = TestWebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        _ = builder.Configuration.AddInMemoryCollection(settings);
        _ = builder.Services.AddGoogleSignInSettings(builder.Configuration);

        WebApplication app = builder.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
