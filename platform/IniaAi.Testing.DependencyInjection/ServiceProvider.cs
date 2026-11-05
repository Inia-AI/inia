using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Services;
using IniaAi.Domain.Core.Interfaces;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MsServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.Testing.DependencyInjection;

public class ServiceProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
{
    private readonly CustomUserDataDbContext _userDataDbContext;
    private readonly CustomCoreDbContext _coreDbContext;

    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;

    private readonly IDbContextFactory<UserDataDbContext> _dbContextFactory;
    private readonly Dictionary<Type, object> _services = [];

    private readonly IUserDataDbContextAccessor _userDataDbContextAccessor;

    public Organization TestOrganization { get; }

    public ServiceProvider()
    {
        string databaseName = $"IniaAi-TestDatabase-{Guid.NewGuid()}";
        // UserData and Core use separate in-memory databases to mirror production (separate physical
        // DBs) and avoid InMemory-provider column-index aliasing on shared entities (Block in both
        // contexts has different ignored properties; sharing storage corrupts reads).
        string userDataDb = $"{databaseName}-userdata";
        string coreDb = $"{databaseName}-core";

        DbContextOptions<UserDataDbContext> userDataOptions = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase(userDataDb)
            .Options;
        _userDataDbContext = new(userDataOptions, userDataDb);

        // Delegate to shared seeding logic (thread-safe, runs once across all test paths)
        TestDatabaseServices.SeedUserDataDatabase(_userDataDbContext);

        // Get the seeded organization
        TestOrganization = _userDataDbContext.Organizations.First(o => o.Name == "IniaTest");

        // Create a minimal service provider for testing
        var services = new ServiceCollection();
        _ = services.AddScoped<UserDataDbContext>(_ => _userDataDbContext);
        _ = services.AddScoped<IDocumentRepository, DocumentRepository>();
        _ = services.AddScoped<IUserRepository, UserRepository>();
        _ = services.AddScoped(typeof(IUserDataRepository<>), typeof(UserDataRepositoryWrapper<>));
        MsServiceProvider serviceProvider = services.BuildServiceProvider();

        _userDataUnitOfWork = new UserDataUnitOfWork(_userDataDbContext, serviceProvider);

        DbContextOptions<CoreDbContext> coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase(coreDb)
            .Options;
        _coreDbContext = new(coreOptions, coreDb);

        // Create a minimal service provider for Core testing
        var coreServices = new ServiceCollection();
        _ = coreServices.AddScoped(typeof(ICoreRepository<>), typeof(CoreRepositoryWrapper<>));
        _ = coreServices.AddScoped<CoreDbContext>(_ => _coreDbContext);
        _ = coreServices.AddScoped<IToolsSettingsRepository, ToolsSettingsRepository>();
        MsServiceProvider coreServiceProvider = coreServices.BuildServiceProvider();

        _coreUnitOfWork = new CoreUnitOfWork(_coreDbContext, coreServiceProvider);

        var factorySource = new CustomDbContextFactorySource<UserDataDbContext>();
        DbContextOptions<UserDataDbContext> userDataOptionsFactory = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase(userDataDb)
            .Options;

#pragma warning disable EF1001 // Internal EF Core API usage.
        _dbContextFactory = new DbContextFactory<UserDataDbContext>(this, userDataOptionsFactory, factorySource: factorySource);
#pragma warning restore EF1001 // Internal EF Core API usage.

        _userDataDbContextAccessor = new UserDataDbContextAccessor(_dbContextFactory);
    }

    public ServiceProvider Add<TInterface>(object service)
    {
        _services[typeof(TInterface)] = service;
        return this;
    }

    public object? GetService(Type serviceType)
    {
        return serviceType switch
        {
            var t when t == typeof(IUserDataUnitOfWork) => _userDataUnitOfWork,
            var t when t == typeof(UserDataDbContext) || t == typeof(IUserDataDbContext) => _userDataDbContext,
            var t when t == typeof(ICoreUnitOfWork) => _coreUnitOfWork,
            var t when t == typeof(CoreDbContext) || t == typeof(ICoreDbContext) => _coreDbContext,
            var t when t == typeof(IUserDataDbContextAccessor) => _userDataDbContextAccessor,
            var t when t == typeof(IDbContextFactory<UserDataDbContext>) => _dbContextFactory,
            var t when t == typeof(IMemoryCache) => new MemoryCache(new MemoryCacheOptions()),
            var t when t == typeof(IServiceScopeFactory) => this,
            var t when t == typeof(IToolsSettingsRepository) => new ToolsSettingsRepository(_coreDbContext),
            var t when _services.TryGetValue(t, out object? service) => service,
            var t when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>) =>
                Array.CreateInstance(t.GetGenericArguments()[0], 0),
            _ => throw new NotImplementedException($"Service of type {serviceType.Name} is not registered in the test service provider."),
        };
    }

    private class CustomUserDataDbContext(DbContextOptions<UserDataDbContext> options, string databaseName)
        : UserDataDbContext(options)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            _ = optionsBuilder.UseInMemoryDatabase(databaseName);
        }
    }

    private class CustomCoreDbContext(DbContextOptions<CoreDbContext> options, string databaseName)
        : CoreDbContext(options)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            _ = optionsBuilder.UseInMemoryDatabase(databaseName);
        }
    }

    public IServiceScope CreateScope()
    {
        return this;
    }

    private bool _disposed = false;
    /// <summary>
    /// Properly disposes of managed resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Follows the standard Dispose pattern.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources (DbContext and UnitOfWork instances)
                _userDataDbContext?.Dispose();
                _coreDbContext?.Dispose();

                if (_userDataUnitOfWork is IDisposable userDataUoW)
                {
                    userDataUoW.Dispose();
                }

                if (_coreUnitOfWork is IDisposable coreUoW)
                {
                    coreUoW.Dispose();
                }

                _services.Clear();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Destructor (Finalizer) to ensure disposal.
    /// </summary>
    ~ServiceProvider()
    {
        Dispose(false);
    }

    IServiceProvider IServiceScope.ServiceProvider => this;
}
