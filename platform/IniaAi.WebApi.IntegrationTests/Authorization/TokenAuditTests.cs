using System.Net;
using IniaAi.Application.Auditing;
using IniaAi.Infrastructure.Persistence.Seed;
using IniaAi.Testing;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// Every token the endpoint issues leaves an authentication audit record naming the grant and the
/// client, a refresh and a service sign-in as much as a password sign-in.
/// </summary>
public class TokenAuditTests
{
    private const string SERVICE_CLIENT_ID = "service_user_iniaai";
    private const string API_SCOPE = "api";

    private readonly CapturingLoggerProvider _logs = new();

    [Fact]
    public async Task ARefresh_IsRecorded_AsASignInByTheRefreshGrant()
    {
        var api = new ClientBuilder(services => services.AddLogging(logging => logging.AddProvider(_logs)));
        await using TestAccount account = await TestAccount.CreateAsync(api);
        (_, string refreshToken) = await account.SignInAsync();

        using HttpResponseMessage refreshed = await account.RefreshAsync(refreshToken);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Single(AuditRecords(AuthenticationAuditKind.SignInSucceeded, AuthenticationAuditMethods.PASSWORD_SIGN_IN), record => record.Message.Contains(account.User.Id, StringComparison.Ordinal));
        Assert.Single(AuditRecords(AuthenticationAuditKind.SignInSucceeded, GrantTypes.RefreshToken), record => record.Message.Contains(account.User.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AServiceSignIn_IsRecorded_AsASignInByTheClientCredentialsGrant()
    {
        var api = new ClientBuilder(services => services.AddLogging(logging => logging.AddProvider(_logs)));
        string credential = api.CurrentScope.ServiceProvider.GetRequiredService<IConfiguration>()[SystemUserClientSecret.CONFIGURATION_KEY]!;

        using HttpResponseMessage response = await api.Client.PostAsync(
            TestAccount.TOKEN_PATH,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.ClientCredentials,
                [Parameters.ClientId] = SERVICE_CLIENT_ID,
                [Parameters.ClientSecret] = credential,
                [Parameters.Scope] = API_SCOPE,
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _ = Assert.Single(AuditRecords(AuthenticationAuditKind.SignInSucceeded, GrantTypes.ClientCredentials));
    }

    private IEnumerable<CapturedLogEntry> AuditRecords(AuthenticationAuditKind kind, string method)
    {
        return _logs.Entries.Where(entry => entry.Category == typeof(LoggingAuthenticationAuditLog).FullName
            && entry.Values.Any(value => value.Key == "AuditKind" && Equals(value.Value, kind))
            && entry.Values.Any(value => value.Key == "AuditMethod" && Equals(value.Value, method)));
    }
}
