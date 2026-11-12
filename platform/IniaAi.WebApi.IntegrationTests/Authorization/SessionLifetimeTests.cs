using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// A session counts from its first sign-in: each refresh carries that time forward, no token outlives
/// the session policy's limit, and the token that reaches the limit comes without a refresh token. A
/// refresh past the limit is refused. The idle lifetime ends a session left unused. The default
/// policy limits nothing. Access tokens live 10 minutes here, and the token server's clock is a test
/// clock, so the tests move time instead of waiting for it.
/// </summary>
public class SessionLifetimeTests
{
    private const int ACCESS_TOKEN_MINUTES = 10;

    private readonly SteppedClock _clock = new();

    [Fact]
    public async Task EachRefresh_CountsFromTheFirstSignIn_AndTheLastTokenComesWithoutARefreshToken()
    {
        ClientBuilder api = Api(new SessionLimit { AbsoluteLimit = TimeSpan.FromMinutes(30) });
        await using TestAccount account = await TestAccount.CreateAsync(api);
        (_, string refreshToken) = await account.SignInAsync();

        for (int refresh = 0; refresh < 2; refresh++)
        {
            _clock.Now += TimeSpan.FromMinutes(8);
            HttpResponseMessage refreshed = await account.RefreshAsync(refreshToken);
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            refreshToken = await TestAccount.ReadTokenAsync(refreshed, Parameters.RefreshToken);
        }

        _clock.Now += TimeSpan.FromMinutes(8);
        HttpResponseMessage last = await account.RefreshAsync(refreshToken);

        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await last.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // Six minutes are left of the thirty. The sign-in time is kept in whole seconds, as auth_time
        // is defined, so the cap lands a little early.
        Assert.InRange(body.RootElement.GetProperty(Parameters.ExpiresIn).GetInt32(), (6 * 60) - 5, 6 * 60);
        Assert.False(body.RootElement.TryGetProperty(Parameters.RefreshToken, out _));
    }

    [Fact]
    public async Task ARefreshPastTheLimit_IsRefused()
    {
        var policy = new SessionLimit { AbsoluteLimit = TimeSpan.FromMinutes(30) };
        ClientBuilder api = Api(policy);
        await using TestAccount account = await TestAccount.CreateAsync(api);
        (_, string refreshToken) = await account.SignInAsync();

        policy.AbsoluteLimit = TimeSpan.FromMinutes(5);
        _clock.Now += TimeSpan.FromMinutes(6);
        HttpResponseMessage refused = await account.RefreshAsync(refreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await TestAccount.ReadTokenAsync(refused, Parameters.Error));
    }

    [Fact]
    public async Task ACustomerSessionRefreshedEveryTwentyDays_EndsAtThirtyDays()
    {
        ClientBuilder api = Api(new SessionLimit { AbsoluteLimit = TimeSpan.FromDays(30), IdleLifetime = TimeSpan.FromDays(30) });
        await using TestAccount account = await TestAccount.CreateAsync(api);
        (_, string refreshToken) = await account.SignInAsync();

        _clock.Now += TimeSpan.FromDays(20);
        HttpResponseMessage twentyDays = await account.RefreshAsync(refreshToken);
        Assert.Equal(HttpStatusCode.OK, twentyDays.StatusCode);
        _clock.Now += TimeSpan.FromDays(11);
        HttpResponseMessage thirtyOneDays = await account.RefreshAsync(await TestAccount.ReadTokenAsync(twentyDays, Parameters.RefreshToken));

        Assert.Equal(HttpStatusCode.BadRequest, thirtyOneDays.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await TestAccount.ReadTokenAsync(thirtyOneDays, Parameters.Error));
    }

    [Fact]
    public async Task ASessionLeftUnusedPastTheIdleLifetime_Ends()
    {
        ClientBuilder api = Api(new SessionLimit { IdleLifetime = TimeSpan.FromMinutes(5) });
        await using TestAccount account = await TestAccount.CreateAsync(api);
        (_, string refreshToken) = await account.SignInAsync();

        _clock.Now += TimeSpan.FromMinutes(4);
        HttpResponseMessage used = await account.RefreshAsync(refreshToken);
        Assert.Equal(HttpStatusCode.OK, used.StatusCode);
        string next = await TestAccount.ReadTokenAsync(used, Parameters.RefreshToken);

        _clock.Now += TimeSpan.FromMinutes(6);
        HttpResponseMessage unused = await account.RefreshAsync(next);

        Assert.Equal(HttpStatusCode.BadRequest, unused.StatusCode);
    }

    [Fact]
    public async Task TheDefaultPolicy_LimitsNothing()
    {
        ClientBuilder api = Api(policy: null);
        TimeSpan refreshLifetime = api.CurrentScope.ServiceProvider.GetRequiredService<IOptions<JwtSettings>>().Value.RefreshTokenLifetime;
        await using TestAccount account = await TestAccount.CreateAsync(api);
        (_, string refreshToken) = await account.SignInAsync();

        _clock.Now += refreshLifetime * 0.9;
        HttpResponseMessage first = await account.RefreshAsync(refreshToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        _clock.Now += refreshLifetime * 0.9;
        HttpResponseMessage second = await account.RefreshAsync(await TestAccount.ReadTokenAsync(first, Parameters.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    private ClientBuilder Api(SessionLimit? policy)
    {
        return new ClientBuilder(services =>
        {
            _ = services.AddSingleton<TimeProvider>(_clock);
            _ = services.Configure<JwtSettings>(jwt => jwt.TokenValidityInMinutes = ACCESS_TOKEN_MINUTES);
            if (policy is not null)
            {
                _ = services.AddSingleton<ISessionLifetimePolicy>(policy);
            }
        });
    }

    private sealed class SessionLimit : ISessionLifetimePolicy
    {
        public TimeSpan? AbsoluteLimit { get; set; }

        public TimeSpan? IdleLifetime { get; init; }

        public ValueTask<SessionLifetime> GetAsync(User user, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new SessionLifetime(AbsoluteLimit, IdleLifetime));
        }
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
