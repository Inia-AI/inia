using IniaAi.Infrastructure.Persistence.Seed;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

public class TokenEndpointTests
{
    private const string TOKEN_PATH = "api/v1/connect/token";
    private const string SERVICE_CLIENT_ID = "service_user_iniaai";
    private const string API_SCOPE = "api";

    private readonly ClientBuilder _api = new();

    private async Task<HttpResponseMessage> RequestTokenAsync(Dictionary<string, string> form)
    {
        return await _api.Client.PostAsync(
            TOKEN_PATH, new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AServiceLoginThatAsksForOfflineAccessGetsNoRefreshToken()
    {
        string secret = _api.CurrentScope.ServiceProvider
            .GetRequiredService<IConfiguration>()[SystemUserClientSecret.CONFIGURATION_KEY]!;

        HttpResponseMessage response = await RequestTokenAsync(new()
        {
            [Parameters.GrantType] = GrantTypes.ClientCredentials,
            [Parameters.ClientId] = SERVICE_CLIENT_ID,
            [Parameters.ClientSecret] = secret,
            [Parameters.Scope] = $"{Scopes.OfflineAccess} {API_SCOPE}",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(body.RootElement.TryGetProperty(Parameters.AccessToken, out _));
        Assert.False(body.RootElement.TryGetProperty(Parameters.RefreshToken, out _));
    }

    [Fact]
    public async Task ASignInThatNamesNoClientReachesTheCredentialCheck()
    {
        HttpResponseMessage response = await RequestTokenAsync(new()
        {
            [Parameters.GrantType] = GrantTypes.Password,
            [Parameters.Username] = "nobody@example.com",
            [Parameters.Password] = Guid.NewGuid().ToString(),
            [Parameters.Scope] = $"{Scopes.OfflineAccess} {API_SCOPE}",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
