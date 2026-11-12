using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.IntegrationTests.Builders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// A real account that signs in through the token endpoint: a user with a password and the
/// User role, an active default membership in its own organization, and any other organizations
/// a test needs. Disposing it removes the user, its tokens and its organizations.
/// </summary>
public sealed class TestAccount : IAsyncDisposable
{
    public const string PASSWORD = "Account-Test-1a";

    public const string TOKEN_PATH = "api/v1/connect/token";
    private const string API_SCOPE = "api";

    private readonly ClientBuilder _api;
    private readonly List<string> _organizationIds;

    private TestAccount(ClientBuilder api, User user, List<string> organizationIds)
    {
        _api = api;
        User = user;
        _organizationIds = organizationIds;
    }

    public User User { get; }

    public string OrganizationId => _organizationIds[0];

    /// <summary>
    /// Creates the account. The first organization is the user's own, a member and open; each
    /// other organization exists and is open or closed as named, and the user belongs to the
    /// ones marked as a membership.
    /// </summary>
    public static async Task<TestAccount> CreateAsync(
        ClientBuilder api,
        params (string Prefix, bool IsOpen, bool IsMember)[] otherOrganizations)
    {
        using IServiceScope scope = api.CurrentScope.ServiceProvider.CreateScope();
        UserManager<User> userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        string ownOrganizationId = $"account-{Guid.NewGuid():N}";
        List<(string Id, bool IsOpen, bool IsMember)> organizations =
            [(ownOrganizationId, true, true), .. otherOrganizations.Select(o => ($"{o.Prefix}-{Guid.NewGuid():N}", o.IsOpen, o.IsMember))];
        foreach ((string id, bool isOpen, _) in organizations)
        {
            _ = db.Organizations.Add(new Organization($"Test organization {id}", id) { IsActive = isOpen });
        }

        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        string email = $"account_{Guid.NewGuid():N}@test.com";
        User user = new() { UserName = email, Email = email, EmailConfirmed = true, OrganizationId = ownOrganizationId };
        IdentityResult created = await userManager.CreateAsync(user, PASSWORD);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
        _ = await userManager.AddToRoleAsync(user, Roles.USER);

        foreach ((string id, _, _) in organizations.Where(o => o.IsMember))
        {
            _ = db.UserOrganizations.Add(new UserOrganization { UserId = user.Id, OrganizationId = id, IsDefault = id == ownOrganizationId });
        }

        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new TestAccount(api, user, [.. organizations.Select(o => o.Id)]);
    }

    public string OrganizationNamed(string prefix)
    {
        return _organizationIds.Single(id => id.StartsWith($"{prefix}-", StringComparison.Ordinal));
    }

    public async Task<(string AccessToken, string RefreshToken)> SignInAsync()
    {
        HttpResponseMessage response = await PostTokenAsync(new()
        {
            [Parameters.GrantType] = GrantTypes.Password,
            [Parameters.Username] = User.Email!,
            [Parameters.Password] = PASSWORD,
            [Parameters.Scope] = $"{Scopes.OfflineAccess} {API_SCOPE}",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await ReadTokenAsync(response, Parameters.AccessToken), await ReadTokenAsync(response, Parameters.RefreshToken));
    }

    public Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        return PostTokenAsync(new()
        {
            [Parameters.GrantType] = GrantTypes.RefreshToken,
            [Parameters.RefreshToken] = refreshToken,
        });
    }

    public async Task<HttpResponseMessage> GetAsync(string path, string? accessToken, string? tenantId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (tenantId is not null)
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        return await _api.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async Task<HttpResponseMessage> PostJsonAsync<TBody>(string path, string? accessToken, TBody body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await _api.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<string> ReadTokenAsync(HttpResponseMessage response, string property)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return body.RootElement.GetProperty(property).GetString()!;
    }

    public async ValueTask DisposeAsync()
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        _ = await db.Tokens.Where(token => token.Subject == User.Id).ExecuteDeleteAsync();
        _ = await db.Authorizations.Where(authorization => authorization.Subject == User.Id).ExecuteDeleteAsync();
        _ = await db.UserRoles.Where(userRole => userRole.UserId == User.Id).ExecuteDeleteAsync();
        _ = await db.UserOrganizations.Where(membership => membership.UserId == User.Id).ExecuteDeleteAsync();
        _ = await db.Users.Where(user => user.Id == User.Id).ExecuteDeleteAsync();
        await _api.CleanOrganizationsByIdsAsync(_organizationIds);
    }

    private async Task<HttpResponseMessage> PostTokenAsync(Dictionary<string, string> form)
    {
        return await _api.Client.PostAsync(TOKEN_PATH, new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
    }
}
