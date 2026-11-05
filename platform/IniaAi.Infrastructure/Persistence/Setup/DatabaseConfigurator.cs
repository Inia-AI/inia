using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration;
using IniaAi.Configuration.Data;
using IniaAi.Domain;
using IniaAi.Domain.Core;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.UserData;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Core.UnitOfWork;
using IniaAi.Infrastructure.Persistence.Seed;
using IniaAi.Infrastructure.Persistence.UserData;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.UserData.UnitOfWork;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace IniaAi.Infrastructure.Persistence.Setup;

public static class DatabaseConfiguratorExtensions
{
    private const int COMMAND_TIMEOUT_SECONDS = 300;

    // Retry window sized to ride out a ~30 s transient outage (RDS storage stall, failover
    // blip) while keeping worst-case time-to-hard-failure bounded to ~1-2 minutes.
    private const int MAX_RETRY_COUNT = 6;
    private const int MAX_RETRY_DELAY_SECONDS = 10;

    // 57P01 admin_shutdown is how a Postgres failover/restart kills established connections;
    // Npgsql does not classify it as transient, so it must be opted in explicitly.
    private static readonly string[] _retriablePostgresErrorCodes = [PostgresErrorCodes.AdminShutdown];

    public static WebApplicationBuilder AddIniaAiDatabases(this WebApplicationBuilder builder)
    {
        IServiceCollection services = builder.Services;
        ConfigurationManager configuration = builder.Configuration;
        PlatformSettings platformSettings = configuration
                .GetSection("PlatformSettings")
                .Get<PlatformSettings>()
            ?? throw new InvalidOperationException("Platform settings not found.");

        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        ILogger<DatabaseMigrationHelper> logger = serviceProvider.GetRequiredService<ILogger<DatabaseMigrationHelper>>();

        SetupUserDataDbContext(logger, services, configuration, platformSettings, builder);
        SetupCoreDbContext(logger, services, configuration, platformSettings, builder);

        logger.LogInformation("DB setup complete.");

        return builder;
    }

    public static WebApplication InitializeDatabases(this WebApplication app)
    {
        using IServiceScope serviceScope = app.Services.CreateScope();

        UserDataDbContext userDataContext = serviceScope
            .ServiceProvider
            .GetRequiredService<UserDataDbContext>();

        CoreDbContext coreContext = serviceScope
            .ServiceProvider
            .GetRequiredService<CoreDbContext>();

        PlatformSettings platformSettings = app.Configuration
            .GetSection("PlatformSettings")
            .Get<PlatformSettings>()
            ?? throw new InvalidOperationException("Platform settings not found.");

        ILogger logger = app.Services.GetRequiredService<ILogger<DatabaseMigrationHelper>>();

        if (PlatformSettings.ShouldReseedDbs)
        {
            using UserDataAdminDbContext userDataAdminContext = serviceScope
                .ServiceProvider
                .GetRequiredService<UserDataAdminDbContext>();

            using CoreAdminDbContext coreAdminContext = serviceScope
                .ServiceProvider
                .GetRequiredService<CoreAdminDbContext>();

            RecreateDatabases(logger, userDataAdminContext, coreAdminContext);
        }

        DatabaseMigrationHelper migrationHelper = app.Services
            .GetRequiredService<DatabaseMigrationHelper>();

        try
        {
            migrationHelper.MigrateDatabase(
                userDataContext,
                () => SeedUserData(
                    logger,
                    userDataContext,
                    platformSettings,
                    app.Configuration,
                    serviceScope.ServiceProvider.GetRequiredService<RoleGrants>(),
                    serviceScope.ServiceProvider.GetRequiredService<BuiltInCounterpartyTypes>()));
        }
        catch (Exception e) when (e is not SeedConfigurationException)
        {
            logger.LogError(e, "An error occurred migrating the UserData database.");

            throw;
        }

        try
        {
            migrationHelper.MigrateDatabase(coreContext, () =>
            {
                SeedCore(
                    logger,
                    coreContext,
                    serviceScope.ServiceProvider.GetServices<ICorePromptsSeed>(),
                    serviceScope.ServiceProvider.GetRequiredService<ModuleWorkflowTypes>());
                RunStartupDataMigrations(serviceScope.ServiceProvider.GetServices<IStartupDataMigration>());
            });
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred while migrating the Core database.");

            throw;
        }

        logger.LogInformation("Database integrity check complete.");
        return app;
    }

    private static void SeedUserData(
        ILogger logger,
        UserDataDbContext userDataContext,
        PlatformSettings platformSettings,
        IConfiguration configuration,
        RoleGrants roles,
        BuiltInCounterpartyTypes counterpartyTypes)
    {
        try
        {
            var userDataDbInitializer = new UserDataDbInitializer(
                logger,
                userDataContext,
                platformSettings.CustomerOrgId,
                SeedAccountPasswords.From(configuration),
                roles,
                counterpartyTypes,
                StaffSeed.From(configuration),
                UserDataDbInitializer.SuperAdminEmailFrom(configuration));
            userDataDbInitializer.Initialize();
        }
        catch (Exception e) when (e is not SeedConfigurationException)
        {
            logger.LogError(e, "An error occurred seeding the UserData database.");
        }
    }

    private static void SeedCore(
        ILogger logger,
        CoreDbContext coreContext,
        IEnumerable<ICorePromptsSeed> modulePromptSeeds,
        ModuleWorkflowTypes moduleWorkflowTypes)
    {
        try
        {
            var coreDbInitializer = new CoreDbInitializer(coreContext, modulePromptSeeds, moduleWorkflowTypes);
            coreDbInitializer.Initialize();
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred while seeding the Core database.");
        }
    }

    private static void RunStartupDataMigrations(IEnumerable<IStartupDataMigration> migrations)
    {
        foreach (IStartupDataMigration migration in migrations)
        {
            migration.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static void RecreateDatabases(
        ILogger logger,
        UserDataAdminDbContext userDataAdminContext,
        CoreAdminDbContext coreAdminContext)
    {
        try
        {
            _ = userDataAdminContext.Database.EnsureDeleted();

            logger.LogInformation("UserData database deleted.");
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                "An error occurred while deleting the UserData database.");
        }

        try
        {
            _ = coreAdminContext.Database.EnsureDeleted();

            logger.LogInformation("Core database deleted.");
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                "An error occurred while deleting the Core database.");
        }

        try
        {
            _ = userDataAdminContext.Database.EnsureCreated();

            logger.LogInformation("UserData database created.");
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                "An error occurred while creating the UserData database.");
        }

        try
        {
            _ = coreAdminContext.Database.EnsureCreated();

            logger.LogInformation("Core database created.");
        }
        catch (Exception e)
        {
            logger.LogError(
                e,
                "An error occurred while creating the Core database.");
        }
    }

    internal static void ConfigureNpgsqlProvider(NpgsqlDbContextOptionsBuilder providerBuilder, string migrationsSchema)
    {
        _ = providerBuilder.MigrationsAssembly("IniaAi.Migrations.PostgreSql");
        _ = providerBuilder.MigrationsHistoryTable(HistoryRepository.DefaultTableName, migrationsSchema);
        _ = providerBuilder.CommandTimeout(COMMAND_TIMEOUT_SECONDS);
        _ = providerBuilder.EnableRetryOnFailure(
            MAX_RETRY_COUNT,
            TimeSpan.FromSeconds(MAX_RETRY_DELAY_SECONDS),
            errorCodesToAdd: _retriablePostgresErrorCodes);
    }

    internal static void ConfigureSqlServerProvider(SqlServerDbContextOptionsBuilder providerBuilder, string migrationsSchema)
    {
        _ = providerBuilder.MigrationsAssembly("IniaAi.Migrations.Mssql");
        _ = providerBuilder.MigrationsHistoryTable(HistoryRepository.DefaultTableName, migrationsSchema);
        _ = providerBuilder.CommandTimeout(COMMAND_TIMEOUT_SECONDS);
        _ = providerBuilder.EnableRetryOnFailure(
            MAX_RETRY_COUNT,
            TimeSpan.FromSeconds(MAX_RETRY_DELAY_SECONDS),
            errorNumbersToAdd: null);
    }

    private static void SetupCoreDbContext(
        ILogger logger,
        IServiceCollection services,
        ConfigurationManager configuration,
        PlatformSettings platformSettings,
        WebApplicationBuilder builder)
    {
        var dbContextOptionsBuilder = new DbContextOptionsBuilder<CoreDbContext>();

        string coreDbConnection = GetDbConnectionString(
            logger,
            configuration,
            platformSettings.CoreDbProvider,
            typeof(CoreDbContext),
            shouldGetAdminConnection: false);

        _ = platformSettings.CoreDbProvider switch
        {
            DatabaseProviders.POSTGRE_SQL => dbContextOptionsBuilder.UseNpgsql(
                coreDbConnection,
                b => ConfigureNpgsqlProvider(b, CoreDbContext.SCHEMA_NAME)),
            DatabaseProviders.MSSQL => dbContextOptionsBuilder.UseSqlServer(
                coreDbConnection,
                b => ConfigureSqlServerProvider(b, CoreDbContext.SCHEMA_NAME)),
            _ => throw new Exception($"Unsupported provider: {platformSettings.CoreDbProvider}"),
        };

        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("LocalDevelopment"))
        {
            ILoggerFactory efLoggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();

            _ = dbContextOptionsBuilder.UseLoggerFactory(efLoggerFactory)
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors();
        }

        // Register Core repositories
        _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
        _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
        // WorkflowTypeCategoryRepository deleted - using generic repository
        _ = services.AddScoped<IToolsSettingsRepository, ToolsSettingsRepository>();

        // Register Core generic repository using Ardalis.Specification
        _ = services.AddScoped(typeof(ICoreRepository<>), typeof(CoreRepositoryWrapper<>));

        _ = services.AddScoped<ICoreUnitOfWork, CoreUnitOfWork>();
        AddOptionsWithSaveInterceptors(services, dbContextOptionsBuilder);
        _ = services.AddDbContext<CoreDbContext>();
        _ = services.AddScoped<ICoreDbContext>(sp => sp.GetRequiredService<CoreDbContext>());

        if (!PlatformSettings.ShouldReseedDbs)
        {
            return;
        }

        string adminDbConnection = GetDbConnectionString(
            logger,
            configuration,
            platformSettings.CoreDbProvider,
            typeof(CoreDbContext),
            shouldGetAdminConnection: true);

        _ = services.AddDbContext<CoreAdminDbContext>(
            options => _ = platformSettings.CoreDbProvider switch
            {
                DatabaseProviders.POSTGRE_SQL => options.UseNpgsql(adminDbConnection, b =>
                {
                    _ = b.MigrationsAssembly("IniaAi.Migrations.PostgreSql");
                    _ = b.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "Core");
                }),

                DatabaseProviders.MSSQL => options.UseSqlServer(adminDbConnection, b =>
                {
                    _ = b.MigrationsAssembly("IniaAi.Migrations.Mssql");
                    _ = b.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "Core");
                }),

                _ => throw new Exception($"Unsupported provider: {platformSettings.CoreDbProvider}")
            });
    }

    private static void SetupUserDataDbContext(
        ILogger logger,
        IServiceCollection services,
        ConfigurationManager configuration,
        PlatformSettings platformSettings,
        WebApplicationBuilder builder)
    {
        string userDataDbConnection = GetDbConnectionString(
            logger,
            configuration,
            platformSettings.UserDataDbProvider,
            typeof(UserDataDbContext),
            shouldGetAdminConnection: false);

        var dbContextOptionsBuilder = new DbContextOptionsBuilder<UserDataDbContext>();

        _ = platformSettings.UserDataDbProvider switch
        {
            DatabaseProviders.POSTGRE_SQL => dbContextOptionsBuilder.UseNpgsql(
                userDataDbConnection,
                b => ConfigureNpgsqlProvider(b, UserDataDbContext.SCHEMA_NAME)),
            DatabaseProviders.MSSQL => dbContextOptionsBuilder.UseSqlServer(
                userDataDbConnection,
                b => ConfigureSqlServerProvider(b, UserDataDbContext.SCHEMA_NAME)),
            _ => throw new Exception($"Unsupported provider: {platformSettings.UserDataDbProvider}"),
        };

        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("LocalDevelopment"))
        {
            ILoggerFactory efLoggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();

            _ = dbContextOptionsBuilder.UseLoggerFactory(efLoggerFactory)
                .EnableSensitiveDataLogging()
                .EnableDetailedErrors();
        }

        AddOptionsWithSaveInterceptors(services, dbContextOptionsBuilder);
        _ = services.AddDbContext<UserDataDbContext>(options =>
        {
            _ = options.UseOpenIddict();
        });

        _ = services.AddScoped<IUserDataDbContext>(sp => sp.GetRequiredService<UserDataDbContext>());
        _ = services.AddScoped<IUserDataDbContextAccessor, UserDataDbContextAccessor>();

        // Register DbContextFactory as Singleton (with explicit options)
        _ = services.AddSingleton<IDbContextFactory<UserDataDbContext>>(serviceProvider =>
        {
            DbContextOptions<UserDataDbContext> options = serviceProvider.GetRequiredService<DbContextOptions<UserDataDbContext>>();
            return new PooledDbContextFactory<UserDataDbContext>(options);
        });

        _ = services.AddScoped<IDocumentRepository, DocumentRepository>();
        _ = services.AddScoped<IFileRepository, FileRepository>();
        _ = services.AddScoped<IFileMetadataRepository, FileMetadataRepository>();
        _ = services.AddScoped<IUserDataWorkflowRepository, UserDataWorkflowRepository>();
        _ = services.AddScoped<IUserRepository, UserRepository>();
        _ = services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        _ = services.AddScoped<IWorkflowExecutionRepository, WorkflowExecutionRepository>();

        // Register UserData generic repository using Ardalis.Specification
        _ = services.AddScoped(typeof(IUserDataRepository<>), typeof(UserDataRepositoryWrapper<>));

        _ = services.AddScoped<IUserDataUnitOfWork, UserDataUnitOfWork>();

        if (!PlatformSettings.ShouldReseedDbs)
        {
            return;
        }

        string adminDbConnection = GetDbConnectionString(
            logger,
            configuration,
            platformSettings.UserDataDbProvider,
            typeof(UserDataDbContext),
            shouldGetAdminConnection: true);

        _ = services.AddDbContext<UserDataAdminDbContext>(
            options => _ = platformSettings.UserDataDbProvider switch
            {
                DatabaseProviders.POSTGRE_SQL => options.UseNpgsql(adminDbConnection, b =>
                {
                    _ = b.MigrationsAssembly("IniaAi.Migrations.PostgreSql");
                    _ = b.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "UserData");
                }),
                DatabaseProviders.MSSQL => options.UseSqlServer(adminDbConnection, b =>
                {
                    _ = b.MigrationsAssembly("IniaAi.Migrations.Mssql");
                    _ = b.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "UserData");
                }),

                _ => throw new Exception($"Unsupported provider: {platformSettings.UserDataDbProvider}")
            });
    }

    private static void AddOptionsWithSaveInterceptors<TContext>(IServiceCollection services, DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        services.TryAddSingleton<IActingUser, NoActingUser>();
        _ = services.AddSingleton(provider => optionsBuilder
            .AddInterceptors(
                new SoftDeleteActorInterceptor(provider.GetRequiredService<IActingUser>()),
                new RerunWriteInterceptor(provider.GetRequiredService<ILogger<RerunWriteInterceptor>>()))
            .Options);
    }

    private static string GetDbConnectionString(
        ILogger logger,
        ConfigurationManager configuration,
        string? provider,
        Type dbContextType,
        bool shouldGetAdminConnection)
    {
        ArgumentNullException.ThrowIfNull(provider);

        string contextName = dbContextType.Name.Replace("DbContext", "");

        string dbConnectionName = GetDbConnectionName(contextName);

        string dbConnection = configuration.GetConnectionString(dbConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{dbConnectionName}' not found.");

        logger.LogInformation(
            "IsInDevEnv: {isInDevEnv}, " +
            "ShouldReseedDbs: {shouldReseedDbs}, " +
            "Using connection string '{connectionName}' " +
            "for {contextName}{Admin}DbContext.",
            PlatformSettings.IsInDevEnv,
            PlatformSettings.ShouldReseedDbs,
            dbConnectionName,
            contextName,
            shouldGetAdminConnection ? "Admin" : "");

        return dbConnection;
    }

    private static string GetDbConnectionName(string contextName)
    {
        return $"{contextName}DbConnection";
    }
}
