using IniaAi.Domain.Entities.StoredKeys;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.StoredKeys;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.StoredKeys;
using IniaAi.Testing.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Infrastructure.Tests.StoredKeys;

public class TenantKeysToStoredKeysMigrationTests : IDisposable
{
    private const string ORG_ID = "org-with-keys";
    private const string PARALLEL_KEY = "fake parallel value";
    private const string PERPLEXITY_KEY = "fake perplexity value";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _userData;
    private readonly CoreDbContext _core;
    private readonly IStoredKeyCipher _cipher;

    public TenantKeysToStoredKeysMigrationTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddDataProtection().UseEphemeralDataProtectionProvider();
        _ = services.AddStoredKeys();
        _serviceProvider = services.BuildServiceProvider();
        _userData = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _core = _serviceProvider.GetRequiredService<CoreDbContext>();
        _cipher = _serviceProvider.GetRequiredService<IStoredKeyCipher>();
    }

    public void Dispose()
    {
        _userData.Dispose();
        _core.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RunAsync_MovesEachTenantKeyIntoAStoredKeyOfTheSameName_AndEmptiesTheField()
    {
        await SeedOrganizationAsync(parallelApiKey: PARALLEL_KEY, perplexityApiKey: $"  {PERPLEXITY_KEY}  ");

        await MigrationRunAsync();

        Assert.Equal(PARALLEL_KEY, await StoredValueAsync("ParallelApiKey"));
        Assert.Equal(PERPLEXITY_KEY, await StoredValueAsync("PerplexityApiKey"));
        Assert.Null(await StoredValueAsync("FmpApiKey"));
        Organization organization = await _userData.Organizations.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(organization.ParallelApiKey);
        Assert.Null(organization.PerplexityApiKey);
        Assert.Null(organization.FmpApiKey);
    }

    [Fact]
    public async Task RunAsync_AStoredKeyThatAlreadyHasTheName_KeepsItsValue()
    {
        await SeedOrganizationAsync(parallelApiKey: PARALLEL_KEY);
        _ = _core.OrganizationStoredKeys.Add(new OrganizationStoredKey
        {
            OrganizationId = ORG_ID,
            Name = "ParallelApiKey",
            ProtectedValue = _cipher.Protect("set-by-an-admin"),
        });
        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);

        await MigrationRunAsync();

        Assert.Equal("set-by-an-admin", await StoredValueAsync("ParallelApiKey"));
        Assert.Null((await _userData.Organizations.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).ParallelApiKey);
    }

    [Fact]
    public async Task RunAsync_ASecondStart_ChangesNothing()
    {
        await SeedOrganizationAsync(parallelApiKey: PARALLEL_KEY);
        await MigrationRunAsync();

        await MigrationRunAsync();

        Assert.Equal(1, await _core.OrganizationStoredKeys.CountAsync(TestContext.Current.CancellationToken));
    }

    private async Task SeedOrganizationAsync(string? parallelApiKey = null, string? perplexityApiKey = null)
    {
        _ = _userData.Organizations.Add(new Organization("Keys Inc", ORG_ID)
        {
            ParallelApiKey = parallelApiKey,
            PerplexityApiKey = perplexityApiKey,
        });
        _ = await _userData.SaveChangesAsync(TestContext.Current.CancellationToken);
        _userData.ChangeTracker.Clear();
    }

    private Task MigrationRunAsync()
    {
        return new TenantKeysToStoredKeysMigration(
                _userData,
                _core,
                _cipher,
                NullLogger<TenantKeysToStoredKeysMigration>.Instance)
            .RunAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> StoredValueAsync(string name)
    {
        string? protectedValue = await _core.OrganizationStoredKeys.AsNoTracking()
            .Where(key => key.OrganizationId == ORG_ID && key.Name == name)
            .Select(key => key.ProtectedValue)
            .SingleOrDefaultAsync(TestContext.Current.CancellationToken);

        return protectedValue is null ? null : _cipher.Unprotect(protectedValue);
    }
}
