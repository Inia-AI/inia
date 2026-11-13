using IniaAi.Common.Constants;
using IniaAi.Configuration.Security;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.Seed;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// The token endpoint issues tokens only through the grants a deployment turned on. Each refusal
/// sits beside the request that succeeds, so a refusal is not a request that could never succeed.
/// </summary>
public class AllowedGrantTypesTests
{
    private const string SERVICE_CLIENT_ID = "service_user_iniaai";
    private const string API_SCOPE = "api";
    private const string OIDC_DISABLED = "oidc_disabled";
    private const string FORGOT_PATH = "api/v1/passwords/forgot";
    private const string RESET_PATH = "api/v1/passwords/reset";
    private const string NEW_PASSWORD = "A-new-password-1!";

    [Fact]
    public async Task ThePasswordGrant_IssuesTokens_WhilePasswordSignInIsOn()
    {
        var api = new ClientBuilder(services => services.Configure<PasswordAuthSettings>(settings => settings.Enabled = true));
        await using TestAccount account = await TestAccount.CreateAsync(api);

        using HttpResponseMessage response = await PostAsync(api, PasswordGrant(account));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await HasAccessTokenAsync(response));
    }

    [Fact]
    public async Task ThePasswordGrant_IssuesNothing_WhenPasswordSignInIsOff()
    {
        var api = new ClientBuilder(services => services.Configure<PasswordAuthSettings>(settings => settings.Enabled = false));
        await using TestAccount account = await TestAccount.CreateAsync(api);

        using HttpResponseMessage response = await PostAsync(api, PasswordGrant(account));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await HasAccessTokenAsync(response));
    }

    [Fact]
    public async Task TheOidcGrant_IsRefused_WhenOidcIsOff()
    {
        var api = new ClientBuilder(services => services.Configure<OidcAuthSettings>(settings => settings.Enabled = false));

        using HttpResponseMessage response = await PostAsync(api, new()
        {
            [Parameters.GrantType] = ExternalLoginProviders.OIDC_GRANT_TYPE,
            [Parameters.IdToken] = "not-a-token",
            [Parameters.Scope] = API_SCOPE,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OIDC_DISABLED, await TestAccount.ReadTokenAsync(response, Parameters.Error));
        Assert.False(await HasAccessTokenAsync(response));
    }

    [Fact]
    public async Task TheServiceClient_GetsATokenOnlyWithItsCredential()
    {
        var api = new ClientBuilder();
        string credential = ServiceCredential(api);

        using HttpResponseMessage withCredential = await PostAsync(api, ServiceGrant(credential));
        using HttpResponseMessage without = await PostAsync(api, ServiceGrant(null));
        using HttpResponseMessage withAnother = await PostAsync(api, ServiceGrant(Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.OK, withCredential.StatusCode);
        Assert.True(await HasAccessTokenAsync(withCredential));
        Assert.False(await HasAccessTokenAsync(without));
        Assert.False(await HasAccessTokenAsync(withAnother));
    }

    [Fact]
    public async Task TheServiceClient_GetsNoToken_WhenTheServiceAccountGrantIsOff()
    {
        ClientBuilder api = ClientBuilder.WithSettings(new Dictionary<string, string>
        {
            [$"{ServiceAccountAuthSettings.SectionName}:{nameof(ServiceAccountAuthSettings.Enabled)}"] = "false",
        });
        string credential = ServiceCredential(api);

        using HttpResponseMessage response = await PostAsync(api, ServiceGrant(credential));

        string description = await TestAccount.ReadTokenAsync(response, Parameters.ErrorDescription);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Errors.UnsupportedGrantType, await TestAccount.ReadTokenAsync(response, Parameters.Error));
        Assert.False(await HasAccessTokenAsync(response), description);
        await TestEvidence.WriteAsync("service-account-grant-off.txt", description + "\n");
    }

    [Fact]
    public async Task TwentyWrongServiceCredentials_ThenTheRightOne_WhatTheEndpointAnswers()
    {
        var api = new ClientBuilder();
        string credential = ServiceCredential(api);
        var wrongAnswers = new List<int>();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            using HttpResponseMessage wrong = await PostAsync(api, ServiceGrant(Guid.NewGuid().ToString()));
            wrongAnswers.Add((int)wrong.StatusCode);
        }

        using HttpResponseMessage right = await PostAsync(api, ServiceGrant(credential));

        string seen = $"wrong: {string.Join(",", wrongAnswers.GroupBy(code => code).Select(group => $"{group.Key}x{group.Count()}"))}; right: {(int)right.StatusCode}";
        await TestEvidence.WriteAsync("allowed-grant-types-answers.txt", seen + "\n");

        Assert.True(right.StatusCode == HttpStatusCode.OK, seen);
    }

    [Theory]
    [InlineData("implicit")]
    [InlineData("token")]
    [InlineData("made_up")]
    public async Task AGrantNoDeploymentOffers_IssuesNothing(string grantType)
    {
        var api = new ClientBuilder();

        using HttpResponseMessage response = await PostAsync(api, new()
        {
            [Parameters.GrantType] = grantType,
            [Parameters.Scope] = API_SCOPE,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await HasAccessTokenAsync(response));
    }

    [Fact]
    public async Task APasswordResetRequest_LeavesAnAccountWithNoPasswordWithout()
    {
        var api = new ClientBuilder(services => services.Configure<PasswordAuthSettings>(settings => settings.Enabled = false));
        await using TestAccount ordinary = await TestAccount.CreateAsync(api);
        User withoutPassword = await AddUserWithoutPasswordAsync(api);
        Assert.True(await HasPasswordAsync(api, ordinary.User.Id));
        Assert.False(await HasPasswordAsync(api, withoutPassword.Id));

        using HttpResponseMessage response = await api.Client.PostAsJsonAsync(FORGOT_PATH, new { email = withoutPassword.Email }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await HasPasswordAsync(api, withoutPassword.Id));
    }

    [Fact]
    public async Task APasswordReset_GivesNoPasswordToAnAccountThatSignsInWithoutOne()
    {
        var api = new ClientBuilder(services => services.Configure<PasswordAuthSettings>(settings => settings.Enabled = false));
        User withoutPassword = await AddUserWithoutPasswordAsync(api);
        string resetToken;
        (IServiceScope scope, UserManager<User> userManager) = api.GetNewScope<UserManager<User>>();
        using (scope)
        {
            resetToken = await userManager.GeneratePasswordResetTokenAsync((await userManager.FindByIdAsync(withoutPassword.Id))!);
        }

        using HttpResponseMessage response = await api.Client.PostAsJsonAsync(
            RESET_PATH, new { email = withoutPassword.Email, token = resetToken, newPassword = NEW_PASSWORD }, TestContext.Current.CancellationToken);

        Assert.False(await HasPasswordAsync(api, withoutPassword.Id), $"The reset answered {(int)response.StatusCode} and the account now has a password.");
    }

    private static async Task<User> AddUserWithoutPasswordAsync(ClientBuilder api)
    {
        (IServiceScope scope, UserManager<User> userManager) = api.GetNewScope<UserManager<User>>();
        using (scope)
        {
            UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            string organizationId = $"nopassword-{Guid.NewGuid():N}";
            _ = db.Organizations.Add(new Organization($"Test organization {organizationId}", organizationId));
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            string email = $"nopassword_{Guid.NewGuid():N}@test.com";
            User user = new() { UserName = email, Email = email, EmailConfirmed = true, OrganizationId = organizationId };
            IdentityResult created = await userManager.CreateAsync(user);
            Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));

            return user;
        }
    }

    private static async Task<bool> HasPasswordAsync(ClientBuilder api, string userId)
    {
        (IServiceScope scope, UserManager<User> userManager) = api.GetNewScope<UserManager<User>>();
        using (scope)
        {
            return await userManager.HasPasswordAsync((await userManager.FindByIdAsync(userId))!);
        }
    }

    private static Dictionary<string, string> PasswordGrant(TestAccount account)
    {
        return new()
        {
            [Parameters.GrantType] = GrantTypes.Password,
            [Parameters.Username] = account.User.Email!,
            [Parameters.Password] = TestAccount.PASSWORD,
            [Parameters.Scope] = $"{Scopes.OfflineAccess} {API_SCOPE}",
        };
    }

    private static string ServiceCredential(ClientBuilder api)
    {
        return api.CurrentScope.ServiceProvider.GetRequiredService<IConfiguration>()[SystemUserClientSecret.CONFIGURATION_KEY]!;
    }

    private static Dictionary<string, string> ServiceGrant(string? credential)
    {
        Dictionary<string, string> form = new()
        {
            [Parameters.GrantType] = GrantTypes.ClientCredentials,
            [Parameters.ClientId] = SERVICE_CLIENT_ID,
            [Parameters.Scope] = API_SCOPE,
        };
        if (credential is not null)
        {
            form[Parameters.ClientSecret] = credential;
        }

        return form;
    }

    private static Task<HttpResponseMessage> PostAsync(ClientBuilder api, Dictionary<string, string> form)
    {
        return api.Client.PostAsync(TestAccount.TOKEN_PATH, new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
    }

    private static async Task<bool> HasAccessTokenAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(Parameters.AccessToken, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
