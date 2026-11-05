using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Core.UnitOfWork;
using IniaAi.Infrastructure.Persistence.UserData;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.UserData.UnitOfWork;
using IniaAi.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Testing.DependencyInjection;

public static class TestDatabaseServices
{
    private static readonly Lock _seedLock = new();

    public static IServiceCollection AddTestDatabaseServices(this IServiceCollection services)
    {
        // Use a unique database name per call to isolate tests running in parallel
        string databaseName = $"IniaAi-TestDatabase-{Guid.NewGuid()}";
        return services.AddTestDatabaseServices(databaseName);
    }

    public static IServiceCollection AddTestDatabaseServices(this IServiceCollection services, string databaseName)
    {
        // UserData and Core use separate in-memory databases to mirror production (separate physical
        // DBs) and avoid InMemory-provider column-index aliasing on shared entities (Block in both
        // contexts has different ignored properties; sharing storage corrupts reads).
        string userDataDb = $"{databaseName}-userdata";
        string coreDb = $"{databaseName}-core";

        _ = services.AddDbContext<UserDataDbContext>(options =>
            options.UseInMemoryDatabase(userDataDb));

        _ = services.AddDbContext<CoreDbContext>(options =>
            options.UseInMemoryDatabase(coreDb));

        // Add DbContext factory for UserData
        _ = services.AddDbContextFactory<UserDataDbContext>(options =>
            options.UseInMemoryDatabase(userDataDb));

        return services.AddTestDatabaseServicesWithoutDbContext();
    }

    /// <summary>
    /// The same services over an in-memory SQLite database for user data: a real relational
    /// provider, so a test can drive code that deletes and updates by set (ExecuteDelete,
    /// ExecuteUpdate) and rely on the foreign keys the schema declares. Each provider built from
    /// the collection opens its own connection, and so gets its own database, which already holds
    /// the schema (<see cref="SqliteUserDataSchema"/>). The connection lives as long as that
    /// provider; disposing the provider closes it and drops the database.
    /// <paramref name="interceptors"/> run on every user-data command.
    /// </summary>
    public static IServiceCollection AddSqliteTestDatabaseServices(this IServiceCollection services, params IInterceptor[] interceptors)
    {
        _ = services.AddSingleton(_ => SqliteUserDataSchema.OpenInMemoryDatabase());
        _ = services.AddSingleton(provider => new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(provider.GetRequiredService<SqliteConnection>())
            .AddInterceptors(interceptors)
            .Options);
        _ = services.AddScoped<UserDataDbContext>(provider =>
            new SqliteUserDataDbContext(provider.GetRequiredService<DbContextOptions<UserDataDbContext>>()));

        string coreDb = $"IniaAi-TestDatabase-{Guid.NewGuid()}-core";
        _ = services.AddDbContext<CoreDbContext>(options => options.UseInMemoryDatabase(coreDb));

        _ = services.AddSingleton<IDbContextFactory<UserDataDbContext>>(provider =>
            new SqliteUserDataDbContextFactory(provider.GetRequiredService<DbContextOptions<UserDataDbContext>>()));

        return services.AddTestDatabaseServicesWithoutDbContext();
    }

    /// <summary>
    /// Adds test database services without registering DbContext.
    /// Use this when you need to register DbContext with a custom database name
    /// (e.g., for test isolation with unique database per test instance).
    /// </summary>
    public static IServiceCollection AddTestDatabaseServicesWithoutDbContext(this IServiceCollection services)
    {
        // Add repositories
        _ = services.AddScoped<IDocumentRepository, DocumentRepository>();
        _ = services.AddScoped<IUserRepository, UserRepository>();
        _ = services.AddScoped<IFileMetadataRepository, FileMetadataRepository>();
        _ = services.AddScoped<IFileRepository, FileRepository>();
        _ = services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        _ = services.AddScoped<IUserDataWorkflowRepository, UserDataWorkflowRepository>();
        _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
        _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
        _ = services.AddScoped<IWorkflowExecutionRepository, WorkflowExecutionRepository>();
        _ = services.AddScoped(typeof(IUserDataRepository<>), typeof(UserDataRepositoryWrapper<>));
        _ = services.AddScoped(typeof(ICoreRepository<>), typeof(CoreRepositoryWrapper<>));
        _ = services.AddScoped<IToolsSettingsRepository, ToolsSettingsRepository>();
        _ = services.AddScoped<ISuperAdminLookup, SuperAdminLookup>();

        // Add Unit of Work patterns
        _ = services.AddScoped<IUserDataUnitOfWork, UserDataUnitOfWork>();
        _ = services.AddScoped<ICoreUnitOfWork, CoreUnitOfWork>();

        // Add context accessor
        _ = services.AddScoped<IUserDataDbContextAccessor, UserDataDbContextAccessor>();

        // Add memory cache (must use AddMemoryCache() to properly register IOptions<MemoryCacheOptions>)
        _ = services.AddMemoryCache();

        return services;
    }

    public static void SeedTestData(IServiceProvider serviceProvider)
    {
        using IServiceScope scope = serviceProvider.CreateScope();

        using UserDataDbContext userDataContext = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        SeedUserDataContext(userDataContext);
    }

    private static void SeedUserDataContext(UserDataDbContext context)
    {
        SeedOnce(
            () => context.Organizations.Any(),
            () =>
            {
                var testOrganization = new Organization("IniaTest");
                _ = context.Organizations.Add(testOrganization);

                var testCounterparty1 = new Counterparty(testOrganization.Id, "TestCounterparty")
                {
                    Email = "testemail1@inia.com",
                };
                _ = context.Counterparties.Add(testCounterparty1);

                var testCounterparty2 = new Counterparty(testOrganization.Id, "TestCounterparty2")
                {
                    Email = "testemail2@inia.com",
                };
                _ = context.Counterparties.Add(testCounterparty2);

                _ = context.SaveChanges();
            });
    }

    /// <summary>
    /// Seeds user data database (Organization + Counterparties).
    /// Thread-safe and idempotent - can be called from multiple test infrastructure paths.
    /// </summary>
    public static void SeedUserDataDatabase(UserDataDbContext context)
    {
        SeedUserDataContext(context);
    }

    /// <summary>
    /// Thread-safe seeding helper. Uses a lock to prevent race conditions
    /// and checks database state to ensure idempotency across different database instances.
    /// </summary>
    /// <param name="alreadyHasData">
    /// A function that checks if the database already contains seeded data.
    /// Must be a Func (not a pre-evaluated bool) because the check must happen INSIDE the lock
    /// to prevent race conditions where two threads both see empty database before either seeds.
    /// </param>
    /// <param name="seed">
    /// The seeding action to execute if database is empty.
    /// Passed as Action to keep seeding logic with its corresponding alreadyHasData check,
    /// making the relationship between check and seed explicit.
    /// </param>
    private static void SeedOnce(Func<bool> alreadyHasData, Action seed)
    {
        // Lock ensures only one thread can seed at a time.
        // The alreadyHasData check MUST be inside the lock - if it were outside,
        // two threads could both see an empty database and both try to seed.
        lock (_seedLock)
        {
            if (alreadyHasData())
            {
                return;
            }

            seed();
        }
    }

    public static Organization GetTestOrganization(IServiceProvider serviceProvider)
    {
        using IServiceScope scope = serviceProvider.CreateScope();
        using UserDataDbContext userDataContext = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        return userDataContext.Organizations.First(o => o.Name == "IniaTest");
    }
}
