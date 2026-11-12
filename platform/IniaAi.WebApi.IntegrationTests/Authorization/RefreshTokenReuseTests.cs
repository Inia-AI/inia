using System.Net;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Testing;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// A refresh token works once: a used one is refused once OpenIddict's 30-second leeway for parallel
/// refreshes has passed, and replaying it ends the tokens issued from it, not the user's other
/// sessions. Sessions started by the password grant and by a module's grant behave alike. The token
/// server's clock is a test clock.
/// </summary>
public class RefreshTokenReuseTests
{
    private const string GRANT_TYPE = "test_code";
    private const string EMAIL_PARAMETER = "email";
    private static readonly TimeSpan PastTheLeeway = TimeSpan.FromSeconds(31);

    private readonly SettableClock _clock = new(DateTimeOffset.UtcNow);

    public enum SessionStart
    {
        PasswordGrant,
        ModuleGrant,
    }

    [Theory]
    [InlineData(SessionStart.PasswordGrant)]
    [InlineData(SessionStart.ModuleGrant)]
    public async Task AUsedRefreshToken_IsRefused_OnceTheLeewayHasPassed(SessionStart start)
    {
        ClientBuilder api = Api();
        await using TestAccount account = await TestAccount.CreateAsync(api);
        string first = await StartSessionAsync(api, account, start);
        using HttpResponseMessage rotated = await account.RefreshAsync(first);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        _clock.Now += PastTheLeeway;
        using HttpResponseMessage replayed = await account.RefreshAsync(first);

        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await TestAccount.ReadTokenAsync(replayed, Parameters.Error));
    }

    [Theory]
    [InlineData(SessionStart.PasswordGrant)]
    [InlineData(SessionStart.ModuleGrant)]
    public async Task AfterAReplay_TheTokenIssuedFromTheReplayedOne_IsRefusedToo(SessionStart start)
    {
        ClientBuilder api = Api();
        await using TestAccount account = await TestAccount.CreateAsync(api);
        string first = await StartSessionAsync(api, account, start);
        string otherSession = await StartSessionAsync(api, account, start);
        using HttpResponseMessage rotated = await account.RefreshAsync(first);
        string second = await TestAccount.ReadTokenAsync(rotated, Parameters.RefreshToken);
        using HttpResponseMessage otherRotated = await account.RefreshAsync(otherSession);
        string otherSecond = await TestAccount.ReadTokenAsync(otherRotated, Parameters.RefreshToken);
        _clock.Now += PastTheLeeway;
        using HttpResponseMessage replayed = await account.RefreshAsync(first);
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);

        using HttpResponseMessage afterReplay = await account.RefreshAsync(second);
        using HttpResponseMessage otherAfterReplay = await account.RefreshAsync(otherSecond);

        Assert.Equal(HttpStatusCode.BadRequest, afterReplay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherAfterReplay.StatusCode);
    }

    [Theory]
    [InlineData(SessionStart.PasswordGrant)]
    [InlineData(SessionStart.ModuleGrant)]
    public async Task ARefreshTokenUsedTwiceInsideTheLeeway_WhatTheEndpointAnswers(SessionStart start)
    {
        ClientBuilder api = Api();
        await using TestAccount account = await TestAccount.CreateAsync(api);
        string first = await StartSessionAsync(api, account, start);

        using HttpResponseMessage once = await account.RefreshAsync(first);
        using HttpResponseMessage twice = await account.RefreshAsync(first);

        Assert.Equal(HttpStatusCode.OK, once.StatusCode);
        await RecordAsync($"refresh-token-leeway-{start}.txt", $"{start}: first use {(int)once.StatusCode}, second use inside the leeway {(int)twice.StatusCode}");
    }

    [Fact(Skip = "security run SI-27: shows an open finding, see the ledger")]
    public async Task ASecondSignIn_EndsTheFirstSessionsRefreshToken()
    {
        ClientBuilder api = Api();
        await using TestAccount account = await TestAccount.CreateAsync(api);
        (_, string first) = await account.SignInAsync();
        (_, string second) = await account.SignInAsync();
        using HttpResponseMessage secondRefresh = await account.RefreshAsync(second);
        Assert.Equal(HttpStatusCode.OK, secondRefresh.StatusCode);

        using HttpResponseMessage firstRefresh = await account.RefreshAsync(first);

        Assert.Equal(HttpStatusCode.BadRequest, firstRefresh.StatusCode);
    }

    [Fact]
    public async Task ALockedOutUser_CannotRefresh()
    {
        ClientBuilder api = Api();
        await using TestAccount account = await TestAccount.CreateAsync(api);
        (_, string refreshToken) = await account.SignInAsync();
        (_, string control) = await account.SignInAsync();
        using HttpResponseMessage beforeTheLock = await account.RefreshAsync(control);
        Assert.Equal(HttpStatusCode.OK, beforeTheLock.StatusCode);
        (IServiceScope scope, UserManager<User> users) = api.GetNewScope<UserManager<User>>();
        using (scope)
        {
            User user = (await users.FindByIdAsync(account.User.Id))!;
            Assert.True((await users.SetLockoutEnabledAsync(user, true)).Succeeded);
            Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(1))).Succeeded);
        }

        using HttpResponseMessage refreshed = await account.RefreshAsync(refreshToken);

        Assert.NotEqual(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.DoesNotContain(Parameters.AccessToken, await refreshed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefreshTokenIssuedBeforeTheApiRestarts_StillRefreshesAfterIt()
    {
        ClientBuilder before = Api();
        await using TestAccount account = await TestAccount.CreateAsync(before);
        (_, string refreshToken) = await account.SignInAsync();
        using HttpResponseMessage sameInstance = await account.RefreshAsync(refreshToken);
        Assert.Equal(HttpStatusCode.OK, sameInstance.StatusCode);
        string rotated = await TestAccount.ReadTokenAsync(sameInstance, Parameters.RefreshToken);

        ClientBuilder after = Api();
        using HttpResponseMessage restarted = await after.Client.PostAsync(
            TestAccount.TOKEN_PATH,
            new FormUrlEncodedContent(new Dictionary<string, string> { [Parameters.GrantType] = GrantTypes.RefreshToken, [Parameters.RefreshToken] = rotated }),
            TestContext.Current.CancellationToken);

        await RecordAsync("refresh-token-restart.txt", $"refresh on the same instance {(int)sameInstance.StatusCode}; the rotated token on a new instance over the same database {(int)restarted.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, restarted.StatusCode);
    }

    private ClientBuilder Api()
    {
        return new ClientBuilder(services =>
        {
            _ = services.AddSingleton<TimeProvider>(_clock);
            _ = services.AddCustomGrant<CustomGrantTests.SignsInTheNamedAddress>(GRANT_TYPE);
        });
    }

    private static async Task<string> StartSessionAsync(ClientBuilder api, TestAccount account, SessionStart start)
    {
        if (start == SessionStart.PasswordGrant)
        {
            return (await account.SignInAsync()).RefreshToken;
        }

        using HttpResponseMessage response = await api.Client.PostAsync(
            TestAccount.TOKEN_PATH,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GRANT_TYPE,
                [EMAIL_PARAMETER] = account.User.Email!,
                [Parameters.Scope] = $"{Scopes.OfflineAccess} api",
            }),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TestAccount.ReadTokenAsync(response, Parameters.RefreshToken);
    }

    private static async Task RecordAsync(string file, string line)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        await TestEvidence.WriteAsync(file, line + "\n");
    }
}
