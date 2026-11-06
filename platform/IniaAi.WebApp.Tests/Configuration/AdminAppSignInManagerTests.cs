using IniaAi.Application.Common.Capabilities;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApp.Configuration;
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
