using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.IntegrationTests.Authorization;

/// <summary>
/// The API hashes a password with the platform's work factor, and a password stored with Identity's
/// older default is hashed again with it at the user's next password sign-in.
/// </summary>
public sealed class PasswordHashUpgradeTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const int IDENTITY_DEFAULT_ITERATIONS = 100_000;

    private readonly ClientBuilder _api;
    private TestAccount _account = default!;

    public PasswordHashUpgradeTests(ApiTestFixture fixture)
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
    public async Task AnAccountCreatedThroughTheApi_StoresAHashWithTheWorkFactor()
    {
        Assert.Equal(PasswordVerificationResult.Success, await VerifyStoredHashAsync());
    }

    [Fact]
    public async Task AHashWithTheOldWorkFactor_IsHashedAgainAtTheNextSignIn()
    {
        await StoreHashAsync(new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = IDENTITY_DEFAULT_ITERATIONS })));
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, await VerifyStoredHashAsync());

        _ = await _account.SignInAsync();

        Assert.Equal(PasswordVerificationResult.Success, await VerifyStoredHashAsync());
    }

    private async Task StoreHashAsync(PasswordHasher<User> hasher)
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        string hash = hasher.HashPassword(_account.User, TestAccount.PASSWORD);
        _ = await db.Users.Where(user => user.Id == _account.User.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.PasswordHash, hash), TestContext.Current.CancellationToken);
    }

    private async Task<PasswordVerificationResult> VerifyStoredHashAsync()
    {
        using IServiceScope scope = _api.CurrentScope.ServiceProvider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        User stored = await db.Users.AsNoTracking().SingleAsync(user => user.Id == _account.User.Id, TestContext.Current.CancellationToken);

        return PasswordHashing.Hasher.VerifyHashedPassword(stored, stored.PasswordHash!, TestAccount.PASSWORD);
    }
}
