using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.AspNetCore.Identity;
using System.Net;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// The token endpoint serves a grant type a module registered: the handler decides who signs in,
/// and the endpoint issues the same tokens as for its own grants. With no handler registered, as in
/// the open build, the same request is refused as an unsupported grant type.
/// </summary>
public class CustomGrantTests
{
    private const string TOKEN_PATH = "api/v1/connect/token";
    private const string GRANT_TYPE = "test_code";
    private const string EMAIL_PARAMETER = "email";

    [Fact]
    public async Task ARegisteredGrant_IssuesTokensForTheUserItsHandlerSignsIn()
    {
        var api = new ClientBuilder(services => services.AddCustomGrant<SignsInTheNamedAddress>(GRANT_TYPE));
        await using TestAccount account = await TestAccount.CreateAsync(api);

        HttpResponseMessage response = await RequestTokenAsync(api, account.User.Email!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(await TestAccount.ReadTokenAsync(response, Parameters.AccessToken)));
        Assert.False(string.IsNullOrEmpty(await TestAccount.ReadTokenAsync(response, Parameters.RefreshToken)));
    }

    [Fact]
    public async Task ARegisteredGrantsRefusal_ComesBackAsItsError()
    {
        var api = new ClientBuilder(services => services.AddCustomGrant<SignsInTheNamedAddress>(GRANT_TYPE));

        HttpResponseMessage response = await RequestTokenAsync(api, "nobody@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await TestAccount.ReadTokenAsync(response, Parameters.Error));
    }

    [Fact]
    public async Task WithNoHandler_TheSameRequestIsAnUnsupportedGrantType()
    {
        var api = new ClientBuilder();
        await using TestAccount account = await TestAccount.CreateAsync(api);

        HttpResponseMessage response = await RequestTokenAsync(api, account.User.Email!);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Errors.UnsupportedGrantType, await TestAccount.ReadTokenAsync(response, Parameters.Error));
    }

    private static Task<HttpResponseMessage> RequestTokenAsync(ClientBuilder api, string email)
    {
        return api.Client.PostAsync(
            TOKEN_PATH,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GRANT_TYPE,
                [EMAIL_PARAMETER] = email,
                [Parameters.Scope] = $"{Scopes.OfflineAccess} api",
            }),
            TestContext.Current.CancellationToken);
    }

    public sealed class SignsInTheNamedAddress(UserManager<User> userManager) : ICustomGrantHandler
    {
        public async Task<CustomGrantResult> HandleAsync(CustomGrantRequest request, CancellationToken cancellationToken)
        {
            User? user = await userManager.FindByEmailAsync(request.Parameters[EMAIL_PARAMETER]!);

            return user is null
                ? CustomGrantResult.Refuse(Errors.InvalidGrant, "No account has that address.")
                : CustomGrantResult.SignIn(user);
        }
    }
}
