using System.Net;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Testing;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// The second factor exists only behind a password: its step needs the short-lived token a correct
/// password earns, so with password sign-in off nothing reaches it, and a made-up token is refused
/// before any account is read.
/// </summary>
public class MfaStepTests
{
    private const string MFA_PATH = "api/v1/connect/token/mfa";
    private const string MFA_GRANT = "mfa";

    [Fact]
    public async Task WithPasswordSignInOn_APasswordThenAnAuthenticatorCode_IssuesTokens()
    {
        var api = new ClientBuilder(services => services.Configure<PasswordAuthSettings>(settings => settings.Enabled = true));
        await using TestAccount account = await TestAccount.CreateAsync(api);
        string key = await EnrolAuthenticatorAsync(api, account.User.Id);

        using HttpResponseMessage password = await PostAsync(api, TestAccount.TOKEN_PATH, PasswordGrant(account));
        Assert.Equal(HttpStatusCode.Accepted, password.StatusCode);
        string mfaToken = await TestAccount.ReadTokenAsync(password, "mfaToken");
        using HttpResponseMessage secondFactor = await PostAsync(api, MFA_PATH, new() { [Parameters.GrantType] = MFA_GRANT, ["mfa_token"] = mfaToken, ["method"] = "totp", ["code"] = AuthenticatorCodes.Current(key) });

        Assert.Equal(HttpStatusCode.OK, secondFactor.StatusCode);
        Assert.False(string.IsNullOrEmpty(await TestAccount.ReadTokenAsync(secondFactor, Parameters.AccessToken)));
    }

    [Fact]
    public async Task WithPasswordSignInOff_NoStepReachesTheSecondFactor()
    {
        var api = new ClientBuilder(services => services.Configure<PasswordAuthSettings>(settings => settings.Enabled = false));
        await using TestAccount account = await TestAccount.CreateAsync(api);
        string key = await EnrolAuthenticatorAsync(api, account.User.Id);

        using HttpResponseMessage password = await PostAsync(api, TestAccount.TOKEN_PATH, PasswordGrant(account));
        using HttpResponseMessage madeUp = await PostAsync(api, MFA_PATH, new() { [Parameters.GrantType] = MFA_GRANT, ["mfa_token"] = "made-up", ["method"] = "totp", ["code"] = AuthenticatorCodes.Current(key) });
        using HttpResponseMessage mfaGrant = await PostAsync(api, TestAccount.TOKEN_PATH, new() { [Parameters.GrantType] = MFA_GRANT, ["mfa_token"] = "made-up", ["code"] = AuthenticatorCodes.Current(key) });

        Assert.Equal(HttpStatusCode.BadRequest, password.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, madeUp.StatusCode);
        Assert.Equal("invalid_mfa_token", await TestAccount.ReadTokenAsync(madeUp, "error"));
        Assert.Equal(HttpStatusCode.BadRequest, mfaGrant.StatusCode);
        Assert.Equal(Errors.UnsupportedGrantType, await TestAccount.ReadTokenAsync(mfaGrant, Parameters.Error));
    }

    private static async Task<string> EnrolAuthenticatorAsync(ClientBuilder api, string userId)
    {
        (IServiceScope scope, UserManager<User> users) = api.GetNewScope<UserManager<User>>();
        using (scope)
        {
            User user = (await users.FindByIdAsync(userId))!;
            Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            return (await users.GetAuthenticatorKeyAsync(user))!;
        }
    }

    private static Dictionary<string, string> PasswordGrant(TestAccount account)
    {
        return new()
        {
            [Parameters.GrantType] = GrantTypes.Password,
            [Parameters.Username] = account.User.Email!,
            [Parameters.Password] = TestAccount.PASSWORD,
            [Parameters.Scope] = $"{Scopes.OfflineAccess} api",
        };
    }

    private static Task<HttpResponseMessage> PostAsync(ClientBuilder api, string path, Dictionary<string, string> form)
    {
        return api.Client.PostAsync(path, new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
    }
}
