using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.UnitOfWork;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.WebApp.Tests.Services;

/// <summary>
/// The administration screen mutates users it loaded when the page opened, through the same scoped
/// context Identity's store writes with. These tests age that snapshot the way a failed login does
/// (a bumped AccessFailedCount and a rotated ConcurrencyStamp) and check each mutation still lands.
/// </summary>
public class UserAccountAdminServiceTests : IDisposable
{
    private const string ORG_ID = "org-admin";
    private const string USER_NAME = "alice";
    private const string NEW_PASSWORD = "Fresh-Passw0rd";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<UserDataDbContext> _options;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;

    public UserAccountAdminServiceTests()
    {
        _connection = SqliteUserDataSchema.OpenInMemoryDatabase();

        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(_connection)
            .Options;

        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(_options));
        _ = services.AddScoped<IUserDataUnitOfWork, UserDataUnitOfWork>();
        _ = services.AddScoped<IUserSessionRevoker, UserSessionRevoker>();
        _ = services.AddScoped<UserAccountAdminService>();
        _ = services.AddIdentityCore<User>().AddEntityFrameworkStores<UserDataDbContext>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _scope.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task UpdatingTheStaleSnapshotDirectly_FailsWithAConcurrencyError()
    {
        User stale = await SeedAndLoadIntoScopeAsync();
        await AgeTheRowLikeAFailedLoginAsync(stale.Id);

        IdentityResult result = await _scope.ServiceProvider.GetRequiredService<UserManager<User>>()
            .UpdateSecurityStampAsync(stale);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
    }

    [Fact]
    public async Task ResetPasswordAsync_OnAStaleSnapshot_SetsThePasswordAndKeepsTheNewerFailedCount()
    {
        User stale = await SeedAndLoadIntoScopeAsync();
        string originalSecurityStamp = stale.SecurityStamp!;
        await AgeTheRowLikeAFailedLoginAsync(stale.Id);

        IdentityResult result = await Service.ResetPasswordAsync(stale, NEW_PASSWORD);

        Assert.True(result.Succeeded, Describe(result));

        User stored = await ReadStoredAsync(stale.Id);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(stored, stored.PasswordHash!, NEW_PASSWORD));
        Assert.NotEqual(originalSecurityStamp, stored.SecurityStamp);
        Assert.Equal(1, stored.AccessFailedCount);
    }

    [Fact]
    public async Task DisableAsync_OnAStaleSnapshot_LocksTheUserOutAndKeepsTheNewerFailedCount()
    {
        User stale = await SeedAndLoadIntoScopeAsync();
        await AgeTheRowLikeAFailedLoginAsync(stale.Id);

        IdentityResult result = await Service.DisableAsync(stale);

        Assert.True(result.Succeeded, Describe(result));

        User stored = await ReadStoredAsync(stale.Id);
        Assert.True(stored.IsDisabled);
        Assert.Equal(1, stored.AccessFailedCount);
    }

    [Fact]
    public async Task ResetPasswordAsync_EndsTheUsersApiSessions()
    {
        User user = await SeedAndLoadIntoScopeAsync();
        await SeedTokenAsync(user.Id, Statuses.Valid);

        IdentityResult result = await Service.ResetPasswordAsync(user, NEW_PASSWORD);

        Assert.True(result.Succeeded, Describe(result));
        await using UserDataDbContext verify = new SqliteUserDataDbContext(_options);
        Assert.Equal([Statuses.Revoked], await verify.Tokens.AsNoTracking().Select(token => token.Status).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetPasswordAsync_APasswordTheRuleRefuses_IsReportedAndChangesNothing()
    {
        User user = await SeedAndLoadIntoScopeAsync();
        string? originalHash = (await ReadStoredAsync(user.Id)).PasswordHash;
        await SeedTokenAsync(user.Id, Statuses.Valid);

        IdentityResult result = await Service.ResetPasswordAsync(user, "Short1a");

        Assert.False(result.Succeeded);
        Assert.Equal(PasswordPolicy.Problems("Short1a"), result.Errors.Select(error => error.Description));
        Assert.Equal(originalHash, (await ReadStoredAsync(user.Id)).PasswordHash);
        await using UserDataDbContext verify = new SqliteUserDataDbContext(_options);
        Assert.Equal([Statuses.Valid], await verify.Tokens.AsNoTracking().Select(token => token.Status).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisableAsync_EndsEveryApiSessionOfThatUserOnly()
    {
        User user = await SeedAndLoadIntoScopeAsync();
        await SeedTokenAsync(user.Id, Statuses.Valid);
        await SeedTokenAsync(user.Id, Statuses.Redeemed);
        await SeedTokenAsync("someone-else", Statuses.Valid);

        IdentityResult result = await Service.DisableAsync(user);

        Assert.True(result.Succeeded, Describe(result));
        await using UserDataDbContext verify = new SqliteUserDataDbContext(_options);
        List<(string? Subject, string? Status)> tokens = [.. (await verify.Tokens.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken))
            .Select(token => (token.Subject, token.Status))
            .OrderBy(token => token.Subject == user.Id ? 0 : 1).ThenBy(token => token.Status)];
        Assert.Equal(
            [(user.Id, Statuses.Redeemed), (user.Id, Statuses.Revoked), ("someone-else", Statuses.Valid)],
            tokens);
    }

    [Fact]
    public async Task EnableAsync_OnAStaleSnapshot_LiftsTheLockout()
    {
        User stale = await SeedAndLoadIntoScopeAsync(user => user.Disable());
        await AgeTheRowLikeAFailedLoginAsync(stale.Id);

        IdentityResult result = await Service.EnableAsync(stale);

        Assert.True(result.Succeeded, Describe(result));
        Assert.False((await ReadStoredAsync(stale.Id)).IsDisabled);
    }

    [Fact]
    public async Task DisableAsync_OnAnUntrackedStaleSnapshot_LocksTheUserOut()
    {
        User stale = await SeedAndLoadIntoScopeAsync(tracked: false);
        await AgeTheRowLikeAFailedLoginAsync(stale.Id);

        IdentityResult result = await Service.DisableAsync(stale);

        Assert.True(result.Succeeded, Describe(result));
        Assert.True((await ReadStoredAsync(stale.Id)).IsDisabled);
    }

    [Fact]
    public async Task ConsecutiveActions_ThroughRefreshedUntrackedLists_BothLand()
    {
        User firstCopy = await SeedAndLoadIntoScopeAsync(tracked: false);
        IdentityResult reset = await Service.ResetPasswordAsync(firstCopy, NEW_PASSWORD);
        Assert.True(reset.Succeeded, Describe(reset));

        User secondCopy = await ScopeContext.Users.AsNoTracking()
            .SingleAsync(user => user.Id == firstCopy.Id, TestContext.Current.CancellationToken);
        IdentityResult disable = await Service.DisableAsync(secondCopy);
        Assert.True(disable.Succeeded, Describe(disable));

        User thirdCopy = await ScopeContext.Users.AsNoTracking()
            .SingleAsync(user => user.Id == firstCopy.Id, TestContext.Current.CancellationToken);
        IdentityResult enable = await Service.EnableAsync(thirdCopy);
        Assert.True(enable.Succeeded, Describe(enable));

        User stored = await ReadStoredAsync(firstCopy.Id);
        Assert.False(stored.IsDisabled);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(stored, stored.PasswordHash!, NEW_PASSWORD));
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTheUserWasDeletedMeanwhile_ReportsItAndLeavesTheContextClean()
    {
        User stale = await SeedAndLoadIntoScopeAsync();
        _ = await ScopeContext.Set<UserOrganization>()
            .SingleAsync(membership => membership.UserId == stale.Id, TestContext.Current.CancellationToken);
        await using (var other = new SqliteUserDataDbContext(_options))
        {
            _ = await other.Users.Where(user => user.Id == stale.Id)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        IdentityResult result = await Service.ResetPasswordAsync(stale, NEW_PASSWORD);

        Assert.False(result.Succeeded);
        IdentityError error = Assert.Single(result.Errors);
        Assert.Equal(UserAccountAdminService.USER_NOT_FOUND_CODE, error.Code);
        Assert.DoesNotContain(ScopeContext.ChangeTracker.Entries(), entry => entry.State == EntityState.Deleted);
        Assert.False(ScopeContext.ChangeTracker.HasChanges());
    }

    private UserAccountAdminService Service => _scope.ServiceProvider.GetRequiredService<UserAccountAdminService>();

    private UserDataDbContext ScopeContext => _scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

    private static string Describe(IdentityResult result)
    {
        return string.Join("; ", result.Errors.Select(error => error.Description));
    }

    private async Task<User> SeedAndLoadIntoScopeAsync(Action<User>? configure = null, bool tracked = true)
    {
        var seeded = new User(ORG_ID)
        {
            UserName = USER_NAME,
            NormalizedUserName = USER_NAME.ToUpperInvariant(),
            Email = "alice@example.test",
            NormalizedEmail = "ALICE@EXAMPLE.TEST",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        seeded.SetPassword("Old-Passw0rd");
        configure?.Invoke(seeded);

        await using (var seed = new SqliteUserDataDbContext(_options))
        {
            _ = await seed.Organizations.AddAsync(new Organization("Admin Org", ORG_ID), TestContext.Current.CancellationToken);
            _ = await seed.Users.AddAsync(seeded, TestContext.Current.CancellationToken);
            _ = await seed.Set<UserOrganization>().AddAsync(
                new UserOrganization { UserId = seeded.Id, OrganizationId = ORG_ID, IsDefault = true },
                TestContext.Current.CancellationToken);
            _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        IQueryable<User> users = tracked ? ScopeContext.Users : ScopeContext.Users.AsNoTracking();

        return await users.SingleAsync(user => user.Id == seeded.Id, TestContext.Current.CancellationToken);
    }

    private async Task AgeTheRowLikeAFailedLoginAsync(string userId)
    {
        await using var other = new SqliteUserDataDbContext(_options);
        User current = await other.Users.SingleAsync(user => user.Id == userId, TestContext.Current.CancellationToken);
        current.AccessFailedCount++;
        current.ConcurrencyStamp = Guid.NewGuid().ToString();
        _ = await other.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<User> ReadStoredAsync(string userId)
    {
        await using var reader = new SqliteUserDataDbContext(_options);

        return await reader.Users.AsNoTracking().SingleAsync(user => user.Id == userId, TestContext.Current.CancellationToken);
    }

    private async Task SeedTokenAsync(string subject, string status)
    {
        await using UserDataDbContext seed = new SqliteUserDataDbContext(_options);
        _ = seed.Tokens.Add(new OpenIddictEntityFrameworkCoreToken { Subject = subject, Status = status, Type = TokenTypeIdentifiers.AccessToken });
        _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
