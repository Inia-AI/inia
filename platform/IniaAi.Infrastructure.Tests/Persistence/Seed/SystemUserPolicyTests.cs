using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.Seed;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqliteUserDataDbContext = IniaAi.Testing.DependencyInjection.SqliteUserDataDbContext;

namespace IniaAi.Infrastructure.Tests.Persistence.Seed;

/// <summary>
/// The seeder gives a system user only to the organizations the policy names, and with no policy
/// registered it gives every organization one, as it always has.
/// </summary>
public sealed class SystemUserPolicyTests : IDisposable
{
    private const string SEEDED = "seeded-org";
    private const string SKIPPED = "skipped-org";

    private readonly SqliteSeedDatabase _database = new();

    public SystemUserPolicyTests()
    {
        using var seed = new SqliteUserDataDbContext(_database.Options);
        _ = seed.Organizations.Add(new Organization("Seeded", SEEDED));
        _ = seed.Organizations.Add(new Organization("Skipped", SKIPPED));
        _ = seed.SaveChanges();
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    [Fact]
    public async Task AnOrganizationThePolicySkips_GetsNoSystemUser()
    {
        await SeedAsync(new SkipsOneOrganization(SKIPPED));

        Assert.Equal([SEEDED], await SystemUserOrganizationsAsync());
    }

    [Fact]
    public async Task WithNoPolicyRegistered_EveryOrganizationGetsASystemUser()
    {
        await SeedAsync(policy: null);

        Assert.Equal([SEEDED, SKIPPED], await SystemUserOrganizationsAsync());
    }

    private async Task SeedAsync(ISystemUserPolicy? policy)
    {
        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(_database.Options));
        _ = services.AddIdentityCore<User>().AddEntityFrameworkStores<UserDataDbContext>();
        _ = services.AddOpenIddict().AddCore(core => core.UseEntityFrameworkCore().UseDbContext<UserDataDbContext>());
        if (policy is not null)
        {
            _ = services.AddSingleton(policy);
        }

        await using ServiceProvider provider = services.BuildServiceProvider();
        await SystemUserSeeder.SeedForAllOrganizationsAsync(provider, new ConfigurationBuilder().Build());
    }

    private async Task<List<string>> SystemUserOrganizationsAsync()
    {
        await using var db = new SqliteUserDataDbContext(_database.Options);
        return await db.Users
            .Where(user => user.IsSystem)
            .Select(user => user.OrganizationId)
            .OrderBy(id => id)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private sealed class SkipsOneOrganization(string skipped) : ISystemUserPolicy
    {
        public bool SeedsSystemUser(Organization organization)
        {
            return organization.Id != skipped;
        }
    }
}
