using IniaAi.Application.Common.Capabilities;
using System.Security.Claims;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApp.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApp.Tests.Configuration;

/// <summary>
/// Every sign-in to the admin app asks whether the user may sign in at all. A user whose organization
/// lacks the admin-app capability is refused there, whichever way they sign in.
/// </summary>
public sealed class AdminAppSignInManagerTests : IAsyncDisposable
{
    private const string PASSWORD = "A-staff-password-1!";
    private const string APPLICATION_COOKIE_PREFIX = ".AspNetCore.Identity.Application=";

    private readonly SqliteConnection _connection = SqliteUserDataSchema.OpenInMemoryDatabase();

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task AUserWhoseOrganizationLacksTheAdminApp_CannotSignIn()
    {
        Assert.False(await CanSignInAsync(new OrganizationCapabilitiesWithout(OrganizationCapabilities.ADMIN_APP)));
    }

    [Fact]
    public async Task AUserWhoseOrganizationHasTheAdminApp_CanSignIn()
    {
        Assert.True(await CanSignInAsync(new EveryCapability()));
    }

    [Fact]
    public async Task ARefusalOfTheAccountItself_StillRefuses_WhateverTheCapabilities()
    {
        Assert.False(await CanSignInAsync(new EveryCapability(), confirmed: false));
    }

    [Fact]
    public async Task ASignInThatSkipsTheCheck_IssuesNoCookie_WhenTheOrganizationLacksTheAdminApp()
    {
        Assert.False(await SignInIssuesCookieAsync(new OrganizationCapabilitiesWithout(OrganizationCapabilities.ADMIN_APP)));
        Assert.True(await SignInIssuesCookieAsync(new EveryCapability()));
    }

    [Fact]
    public async Task AStaffMember_WhoseDefaultOrganizationLacksTheAdminApp_StillSignsIn()
    {
        Assert.True(await SignInIssuesCookieAsync(new OrganizationCapabilitiesWithout(OrganizationCapabilities.ADMIN_APP), role: Roles.SUPER_ADMIN));
    }

    public enum SignInMethod
    {
        Password,
        AuthenticatorCode,
        RecoveryCode,
        Refresh,
    }

    [Theory]
    [InlineData(SignInMethod.Password)]
    [InlineData(SignInMethod.AuthenticatorCode)]
    [InlineData(SignInMethod.RecoveryCode)]
    [InlineData(SignInMethod.Refresh)]
    public async Task EachSignInMethod_IssuesNoCookie_ToAUserWhoseOrganizationLacksTheAdminApp(SignInMethod method)
    {
        Assert.False((await SignInThroughAsync(method, new OrganizationCapabilitiesWithout(OrganizationCapabilities.ADMIN_APP))).IssuedCookie);
    }

    [Theory]
    [InlineData(SignInMethod.Password)]
    [InlineData(SignInMethod.AuthenticatorCode)]
    [InlineData(SignInMethod.RecoveryCode)]
    [InlineData(SignInMethod.Refresh)]
    public async Task EachSignInMethod_IssuesTheCookie_ToAUserWhoseOrganizationHasTheAdminApp(SignInMethod method)
    {
        Assert.True((await SignInThroughAsync(method, new EveryCapability())).IssuedCookie);
    }

    [Fact]
    public async Task ARefusedRecoveryCodeSignIn_ReportsNotAllowed_AndLeavesTheCodeUnspent()
    {
        SignInOutcome refused = await SignInThroughAsync(SignInMethod.RecoveryCode, new OrganizationCapabilitiesWithout(OrganizationCapabilities.ADMIN_APP));
        SignInOutcome admitted = await SignInThroughAsync(SignInMethod.RecoveryCode, new EveryCapability());

        Assert.Equal((SignInResult.NotAllowed, 1), (refused.Result, refused.RecoveryCodesLeft));
        Assert.Equal((SignInResult.Success, 0), (admitted.Result, admitted.RecoveryCodesLeft));
    }

    private sealed record SignInOutcome(bool IssuedCookie, SignInResult? Result, int RecoveryCodesLeft);

    /// <summary>
    /// Signs in through one method as the admin app's account pages do, over two requests where the
    /// method needs a step before it. The step before (a password that passed, or a session issued
    /// earlier) is issued straight into its cookie, as for a user whose organization lost the admin
    /// app between the two steps.
    /// </summary>
    private async Task<SignInOutcome> SignInThroughAsync(SignInMethod method, IOrganizationCapabilities capabilities)
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddHttpContextAccessor();
        _ = services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        _ = services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>().UseSqlite(_connection).Options));
        _ = services.AddSingleton(capabilities);
        _ = services.AddIdentityCore<User>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<UserDataDbContext>()
            .AddSignInManager<AdminAppSignInManager>()
            .AddDefaultTokenProviders();
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();

        User user;
        string? recoveryCode = null;
        string cookies;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            HttpContext http = StartRequest(scope, cookies: null);
            UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            var organization = new Organization("Company");
            _ = db.Organizations.Add(organization);
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            string address = $"analyst-{organization.Id}@company.com";
            user = new User { UserName = address, Email = address, EmailConfirmed = true, OrganizationId = organization.Id };
            UserManager<User> users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.True((await users.CreateAsync(user, PASSWORD)).Succeeded);

            switch (method)
            {
                case SignInMethod.Password:
                    SignInResult passwordResult = await scope.ServiceProvider.GetRequiredService<SignInManager<User>>().PasswordSignInAsync(user, PASSWORD, isPersistent: false, lockoutOnFailure: false);
                    return new(IssuedSessionCookie(http.Response), passwordResult, 0);
                case SignInMethod.AuthenticatorCode or SignInMethod.RecoveryCode:
                    Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
                    Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
                    recoveryCode = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();
                    await http.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, Principal(IdentityConstants.TwoFactorUserIdScheme, user.Id));
                    break;
                case SignInMethod.Refresh:
                    await http.SignInAsync(IdentityConstants.ApplicationScheme, Principal(IdentityConstants.ApplicationScheme, user.Id));
                    break;
            }

            cookies = string.Join("; ", http.Response.Headers.SetCookie.Select(cookie => cookie!.Split(';')[0]));
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            HttpContext http = StartRequest(scope, cookies);
            SignInManager<User> signIn = scope.ServiceProvider.GetRequiredService<SignInManager<User>>();
            UserManager<User> users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            User stored = (await users.FindByIdAsync(user.Id))!;
            SignInResult? result = null;
            switch (method)
            {
                case SignInMethod.AuthenticatorCode:
                    result = await signIn.TwoFactorAuthenticatorSignInAsync(AuthenticatorCodes.Current((await users.GetAuthenticatorKeyAsync(stored))!), isPersistent: false, rememberClient: false);
                    break;
                case SignInMethod.RecoveryCode:
                    result = await signIn.TwoFactorRecoveryCodeSignInAsync(recoveryCode!);
                    break;
                case SignInMethod.Refresh:
                    await signIn.RefreshSignInAsync(stored);
                    break;
            }

            return new(IssuedSessionCookie(http.Response), result, await users.CountRecoveryCodesAsync(await users.FindByIdAsync(user.Id) ?? stored));
        }
    }

    private static HttpContext StartRequest(AsyncServiceScope scope, string? cookies)
    {
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (cookies is not null)
        {
            http.Request.Headers.Cookie = cookies;
        }

        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        return http;
    }

    private static ClaimsPrincipal Principal(string scheme, string userId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, userId), new Claim(ClaimTypes.NameIdentifier, userId)], scheme));
    }

    private static bool IssuedSessionCookie(HttpResponse response)
    {
        return response.Headers.SetCookie.Any(cookie => cookie!.StartsWith(APPLICATION_COOKIE_PREFIX, StringComparison.Ordinal)
            && !cookie.StartsWith($"{APPLICATION_COOKIE_PREFIX};", StringComparison.Ordinal));
    }

    private async Task<bool> SignInIssuesCookieAsync(IOrganizationCapabilities capabilities, string? role = null)
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddHttpContextAccessor();
        _ = services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme);
        _ = services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>().UseSqlite(_connection).Options));
        _ = services.AddSingleton(capabilities);
        _ = services.AddIdentityCore<User>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<UserDataDbContext>()
            .AddSignInManager<AdminAppSignInManager>();
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        var organization = new Organization("Company");
        _ = db.Organizations.Add(organization);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        string address = $"analyst-{organization.Id}@company.com";
        var user = new User { UserName = address, Email = address, EmailConfirmed = true, OrganizationId = organization.Id };
        UserManager<User> users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.True((await users.CreateAsync(user)).Succeeded);
        if (role is not null)
        {
            RoleManager<IdentityRole> roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            if (!await roles.RoleExistsAsync(role))
            {
                _ = await roles.CreateAsync(new IdentityRole(role));
            }

            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }

        await scope.ServiceProvider.GetRequiredService<SignInManager<User>>().SignInAsync(user, isPersistent: false);

        return http.Response.Headers.SetCookie.Count > 0;
    }

    private async Task<bool> CanSignInAsync(IOrganizationCapabilities capabilities, bool confirmed = true)
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddHttpContextAccessor();
        _ = services.AddAuthentication();
        _ = services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(new DbContextOptionsBuilder<UserDataDbContext>().UseSqlite(_connection).Options));
        _ = services.AddSingleton(capabilities);
        _ = services.AddIdentityCore<User>(options => options.SignIn.RequireConfirmedEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<UserDataDbContext>()
            .AddSignInManager<AdminAppSignInManager>();
        await using Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        var user = new User { UserName = "analyst@company.com", Email = "analyst@company.com", EmailConfirmed = confirmed, OrganizationId = "org-1" };
        return await scope.ServiceProvider.GetRequiredService<SignInManager<User>>().CanSignInAsync(user);
    }
}
