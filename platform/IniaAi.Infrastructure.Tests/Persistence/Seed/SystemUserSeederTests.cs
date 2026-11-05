using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.Seed;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using SqliteUserDataDbContext = IniaAi.Testing.DependencyInjection.SqliteUserDataDbContext;
using SqliteUserDataSchema = IniaAi.Testing.DependencyInjection.SqliteUserDataSchema;

namespace IniaAi.Infrastructure.Tests.Persistence.Seed;

/// <summary>
/// The seeder runs on every boot against whatever the previous boot left in the OpenIddict
/// application store. These tests drive it through the real EF Core stores on SQLite, so a
/// descriptor OpenIddict would reject in production is rejected here too.
/// </summary>
public class SystemUserSeederTests : IDisposable
{
    private const string ORG_ID = "Acme";
    private const string ORG_NAME = "Acme Corp";
    private const string REAL_SECRET = "a-real-secret-0123456789";
    private const string ROTATED_SECRET = "another-real-secret-9876543210";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _clientId;

    public SystemUserSeederTests()
    {
        _connection = SqliteUserDataSchema.OpenInMemoryDatabase();

        DbContextOptions<UserDataDbContext> options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(_connection)
            .Options;

        using (var seed = new SqliteUserDataDbContext(options))
        {
            var org = new Organization(ORG_NAME, ORG_ID);
            _ = seed.Organizations.Add(org);
            _ = seed.SaveChanges();
            _clientId = SystemUserSeeder.GetClientId(org);
        }

        var services = new ServiceCollection();
        _ = services.AddLogging();
        _ = services.AddScoped<UserDataDbContext>(_ => new SqliteUserDataDbContext(options));
        _ = services.AddIdentityCore<User>().AddEntityFrameworkStores<UserDataDbContext>();
        _ = services.AddOpenIddict().AddCore(core => core.UseEntityFrameworkCore().UseDbContext<UserDataDbContext>());

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task FirstSeed_CreatesAConfidentialClientThatValidatesTheSecret()
    {
        await SeedAsync(REAL_SECRET);

        await AssertConfidentialClientAcceptsAsync(REAL_SECRET);
    }

    [Fact]
    public async Task ReseedingWithTheSameSecret_IsIdempotent()
    {
        await SeedAsync(REAL_SECRET);

        await SeedAsync(REAL_SECRET);

        await AssertConfidentialClientAcceptsAsync(REAL_SECRET);
        Assert.Equal(1, await CountApplicationsAsync());
    }

    [Fact]
    public async Task ReseedingWithARotatedSecret_ReplacesIt()
    {
        await SeedAsync(REAL_SECRET);

        await SeedAsync(ROTATED_SECRET);

        await AssertConfidentialClientAcceptsAsync(ROTATED_SECRET);
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        IOpenIddictApplicationManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        object application = await FindApplicationAsync(manager);
        Assert.False(await manager.ValidateClientSecretAsync(application, REAL_SECRET, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyConfiguredSecret_SeedsTheUserButNoClient_AndTheNextSeedWithARealSecretCreatesAConfidentialOne()
    {
        await SeedAsync(string.Empty);

        Assert.Equal(0, await CountApplicationsAsync());
        Assert.NotNull(await FindSystemUserAsync());

        await SeedAsync(REAL_SECRET);

        await AssertConfidentialClientAcceptsAsync(REAL_SECRET);
    }

    [Fact]
    public async Task WhitespaceConfiguredSecret_IsTreatedLikeAnEmptyOne()
    {
        await SeedAsync("   ");

        Assert.Equal(0, await CountApplicationsAsync());
    }

    [Fact]
    public async Task MissingSecretKey_SeedsTheUserButNoClient()
    {
        await SeedAsync(null);

        Assert.Equal(0, await CountApplicationsAsync());
        Assert.NotNull(await FindSystemUserAsync());
    }

    [Fact]
    public async Task TheSystemUser_HasNoPassword()
    {
        await SeedAsync(REAL_SECRET);

        User? systemUser = await FindSystemUserAsync();
        Assert.NotNull(systemUser);
        Assert.Null(systemUser.PasswordHash);
    }

    [Fact]
    public async Task ASystemUserAnEarlierBuildGaveAPassword_LosesIt()
    {
        await using (AsyncServiceScope setup = _serviceProvider.CreateAsyncScope())
        {
            UserManager<User> userManager = setup.ServiceProvider.GetRequiredService<UserManager<User>>();
            var earlier = new User
            {
                UserName = _clientId,
                Email = $"{_clientId}@system.example",
                IsSystem = true,
                OrganizationId = ORG_ID,
            };
            Assert.True((await userManager.CreateAsync(earlier, "An-earlier-build's-password-1")).Succeeded);
        }

        await SeedAsync(REAL_SECRET);

        User? systemUser = await FindSystemUserAsync();
        Assert.NotNull(systemUser);
        Assert.Null(systemUser.PasswordHash);
    }

    [Fact]
    public async Task AClientStoredAsPublic_IsConvertedToConfidentialWithTheNewSecret()
    {
        // The shape the seeder left behind before this fix when the configured secret was
        // empty: OpenIddict infers a public client from the missing secret.
        await using (AsyncServiceScope setup = _serviceProvider.CreateAsyncScope())
        {
            IOpenIddictApplicationManager manager = setup.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            object created = await manager.CreateAsync(
                new OpenIddictApplicationDescriptor
                {
                    ClientId = _clientId,
                    ClientSecret = string.Empty,
                    Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.Password },
                },
                TestContext.Current.CancellationToken);
            Assert.Equal(ClientTypes.Public, await manager.GetClientTypeAsync(created, TestContext.Current.CancellationToken));
        }

        await SeedAsync(REAL_SECRET);

        await AssertConfidentialClientAcceptsAsync(REAL_SECRET);
        Assert.Equal(1, await CountApplicationsAsync());
    }

    private async Task SeedAsync(string? configuredSecret)
    {
        var settings = new Dictionary<string, string?>();
        if (configuredSecret is not null)
        {
            settings[SystemUserClientSecret.CONFIGURATION_KEY] = configuredSecret;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        await SystemUserSeeder.SeedForAllOrganizationsAsync(_serviceProvider, configuration);
    }

    private async Task AssertConfidentialClientAcceptsAsync(string secret)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        IOpenIddictApplicationManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        object application = await FindApplicationAsync(manager);

        Assert.Equal(ClientTypes.Confidential, await manager.GetClientTypeAsync(application));
        Assert.True(await manager.ValidateClientSecretAsync(application, secret));
        Assert.Contains(Permissions.GrantTypes.Password, await manager.GetPermissionsAsync(application));
        Assert.Contains(Permissions.GrantTypes.ClientCredentials, await manager.GetPermissionsAsync(application));
    }

    private async Task<object> FindApplicationAsync(IOpenIddictApplicationManager manager)
    {
        object? application = await manager.FindByClientIdAsync(_clientId);
        Assert.NotNull(application);
        return application;
    }

    private async Task<long> CountApplicationsAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        IOpenIddictApplicationManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return await manager.CountAsync();
    }

    private async Task<User?> FindSystemUserAsync()
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        UserManager<User> userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        return await userManager.FindByNameAsync(_clientId);
    }
}
