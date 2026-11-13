using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.Contracts.Requests.User;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// An account change that takes access away, and a new password, end the user's API sessions at
/// once, and a refresh hands out the user's current roles rather than the ones the first sign-in
/// granted.
/// </summary>
public sealed class SessionRevocationTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string PROBE_PATH = "api/v1/users/me/tenants";
    private const string CHANGE_PASSWORD_PATH = "api/v1/profiles/change-password";
    private const string RESET_PASSWORD_PATH = "api/v1/passwords/reset";
    private const string SIGN_OUT_PATH = "api/v1/connect/revoke";
    private const string NEW_PASSWORD = "Changed-Test-2b";
    private const string WRONG_PASSWORD = TestAccount.PASSWORD + "-not-it";

    private readonly ClientBuilder _api;
    private TestAccount _account = default!;

    public SessionRevocationTests(ApiTestFixture fixture)
    {
        _api = fixture.ClientBuilder;
    }

    public async ValueTask InitializeAsync()
    {
        _account = await TestAccount.CreateAsync(_api);
    }

    public async ValueTask DisposeAsync()
    {
        await _account.DisposeAsync();
    }

    [Fact]
    public async Task ARevokedAccessToken_IsRefusedOnItsNextRequest()
    {
        (string accessToken, _) = await _account.SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await _account.GetAsync(PROBE_PATH, accessToken)).StatusCode);

        await RevokeAllAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _account.GetAsync(PROBE_PATH, accessToken)).StatusCode);
    }

    [Fact]
    public async Task ARevokedRefreshToken_OpensNoNewSession()
    {
        (_, string refreshToken) = await _account.SignInAsync();

        await RevokeAllAsync();

        HttpResponseMessage refreshed = await _account.RefreshAsync(refreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, refreshed.StatusCode);
    }

    [Fact]
    public async Task ChangingTheirOwnPassword_EndsEverySessionOfTheUser()
    {
        (string accessToken, string refreshToken) = await _account.SignInAsync();
        (string otherDeviceToken, _) = await _account.SignInAsync();

        HttpResponseMessage changed = await _account.PostJsonAsync(
            CHANGE_PASSWORD_PATH,
            accessToken,
            new ChangePasswordRequest { CurrentPassword = TestAccount.PASSWORD, NewPassword = NEW_PASSWORD });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _account.GetAsync(PROBE_PATH, accessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _account.GetAsync(PROBE_PATH, otherDeviceToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _account.RefreshAsync(refreshToken)).StatusCode);
    }

    [Fact]
    public async Task AFailedPasswordChange_KeepsTheSessions()
    {
        (string accessToken, _) = await _account.SignInAsync();

        HttpResponseMessage refused = await _account.PostJsonAsync(
            CHANGE_PASSWORD_PATH,
            accessToken,
            new ChangePasswordRequest { CurrentPassword = WRONG_PASSWORD, NewPassword = NEW_PASSWORD });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _account.GetAsync(PROBE_PATH, accessToken)).StatusCode);
    }

    [Fact]
    public async Task AResetFromTheMailedLink_EndsEverySessionOfTheUser()
    {
        (string accessToken, string refreshToken) = await _account.SignInAsync();
        string resetToken;
        using (IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope())
        {
            UserManager<User> userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            resetToken = await userManager.GeneratePasswordResetTokenAsync((await userManager.FindByIdAsync(_account.User.Id))!);
        }

        HttpResponseMessage reset = await _account.PostJsonAsync(
            RESET_PASSWORD_PATH,
            accessToken: null,
            new ResetPasswordRequest { Email = _account.User.Email!, Token = resetToken, NewPassword = NEW_PASSWORD });

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _account.GetAsync(PROBE_PATH, accessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _account.RefreshAsync(refreshToken)).StatusCode);
    }

    [Fact]
    public async Task SigningOut_EndsThatSessionOnTheServer_AndNoOther()
    {
        (string accessToken, string refreshToken) = await _account.SignInAsync();
        (string otherDeviceToken, string otherDeviceRefreshToken) = await _account.SignInAsync();

        HttpResponseMessage signedOut = await SignOutAsync(refreshToken, TokenTypeHints.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _account.GetAsync(PROBE_PATH, accessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _account.RefreshAsync(refreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _account.GetAsync(PROBE_PATH, otherDeviceToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _account.RefreshAsync(otherDeviceRefreshToken)).StatusCode);
    }

    [Fact]
    public async Task SigningOutWithTheAccessToken_EndsTheRefreshTokenOfThatSessionToo()
    {
        (string accessToken, string refreshToken) = await _account.SignInAsync();

        HttpResponseMessage signedOut = await SignOutAsync(accessToken, TokenTypeHints.AccessToken);

        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _account.GetAsync(PROBE_PATH, accessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _account.RefreshAsync(refreshToken)).StatusCode);
    }

    [Fact]
    public async Task SigningOut_AfterARefresh_EndsTheTokensTheRefreshIssued()
    {
        (string firstAccessToken, string firstRefreshToken) = await _account.SignInAsync();
        HttpResponseMessage refreshed = await _account.RefreshAsync(firstRefreshToken);
        string accessToken = await TestAccount.ReadTokenAsync(refreshed, Parameters.AccessToken);
        string refreshToken = await TestAccount.ReadTokenAsync(refreshed, Parameters.RefreshToken);

        HttpResponseMessage signedOut = await SignOutAsync(refreshToken, TokenTypeHints.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _account.GetAsync(PROBE_PATH, accessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _account.GetAsync(PROBE_PATH, firstAccessToken)).StatusCode);
    }

    [Fact]
    public async Task SigningOutAgainWithTheSameToken_AnswersOkAsRfc7009Asks()
    {
        (_, string refreshToken) = await _account.SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await SignOutAsync(refreshToken, TokenTypeHints.RefreshToken)).StatusCode);

        HttpResponseMessage again = await SignOutAsync(refreshToken, TokenTypeHints.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task ARefresh_CarriesTheRolesTheUserHoldsNow()
    {
        (_, string refreshToken) = await _account.SignInAsync();
        using (IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope())
        {
            UserManager<User> userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            User user = (await userManager.FindByIdAsync(_account.User.Id))!;
            _ = await userManager.AddToRoleAsync(user, Roles.ADMIN);
            _ = await userManager.RemoveFromRoleAsync(user, Roles.USER);
        }

        HttpResponseMessage refreshed = await _account.RefreshAsync(refreshToken);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        string accessToken = await TestAccount.ReadTokenAsync(refreshed, Parameters.AccessToken);
        Assert.Equal([Roles.ADMIN], RolesIn(accessToken));
    }

    private async Task<HttpResponseMessage> SignOutAsync(string token, string tokenTypeHint)
    {
        return await _api.Client.PostAsync(
            SIGN_OUT_PATH,
            new FormUrlEncodedContent(new Dictionary<string, string> { [Parameters.Token] = token, [Parameters.TokenTypeHint] = tokenTypeHint }),
            TestContext.Current.CancellationToken);
    }

    private async Task RevokeAllAsync()
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IUserSessionRevoker>()
            .RevokeAllAsync(_account.User.Id, TestContext.Current.CancellationToken);
    }

    private static List<string> RolesIn(string accessToken)
    {
        return [.. new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims
            .Where(claim => claim.Type == Claims.Role)
            .Select(claim => claim.Value)];
    }
}
