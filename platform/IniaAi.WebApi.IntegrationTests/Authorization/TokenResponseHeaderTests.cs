using System.Net;
using IniaAi.Configuration.Security;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// No browser or proxy may keep a token response: the tokens it carries, and the refusal, both come
/// with <c>Cache-Control: no-store</c>.
/// </summary>
public class TokenResponseHeaderTests
{

    [Fact]
    public async Task AnIssuedTokenAndARefusal_AreBothMarkedNoStore()
    {
        var api = new ClientBuilder();
        await using TestAccount account = await TestAccount.CreateAsync(api);

        using HttpResponseMessage issued = await PasswordGrantAsync(api, account.User.Email!, TestAccount.PASSWORD);
        using HttpResponseMessage refused = await PasswordGrantAsync(api, account.User.Email!, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.True(issued.Headers.CacheControl?.NoStore, $"Issued: Cache-Control is '{issued.Headers.CacheControl}'.");
        Assert.NotEqual(HttpStatusCode.OK, refused.StatusCode);
        Assert.True(refused.Headers.CacheControl?.NoStore, $"Refused: Cache-Control is '{refused.Headers.CacheControl}'.");
    }

    [Fact]
    public async Task ARefusalTheControllerWritesItself_IsMarkedNoStoreToo()
    {
        var api = new ClientBuilder(services => services.Configure<PasswordAuthSettings>(settings => settings.Enabled = false));
        await using TestAccount account = await TestAccount.CreateAsync(api);

        using HttpResponseMessage refused = await PasswordGrantAsync(api, account.User.Email!, TestAccount.PASSWORD);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.True(refused.Headers.CacheControl?.NoStore, $"Cache-Control is '{refused.Headers.CacheControl}'.");
    }

    private static Task<HttpResponseMessage> PasswordGrantAsync(ClientBuilder api, string email, string password)
    {
        return api.Client.PostAsync(
            TestAccount.TOKEN_PATH,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.Password,
                [Parameters.Username] = email,
                [Parameters.Password] = password,
                [Parameters.Scope] = $"{Scopes.OfflineAccess} api",
            }),
            TestContext.Current.CancellationToken);
    }
}
