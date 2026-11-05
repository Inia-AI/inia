using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Core;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.Core.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MsServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// An in-memory SQLite Core database with the schema and a real unit of work over it, for a test
/// that needs what the in-memory provider skips: foreign keys, cascades and transactions that roll
/// back. The schema is built once per test process and copied into each database, as
/// <see cref="SqliteUserDataSchema"/> does for user data.
/// </summary>
public sealed class SqliteCoreDatabase : IDisposable
{
    private const string IN_MEMORY = "DataSource=:memory:";

    private static readonly Lazy<SqliteConnection> _template = new(CreateTemplate);
    private static readonly Lock _copyLock = new();

    private readonly SqliteConnection _connection;
    private readonly MsServiceProvider _repositories;

    public SqliteCoreDatabase(params IInterceptor[] interceptors)
        : this(options => new SqliteCoreDbContext(options), interceptors)
    {
    }

    /// <param name="createContext">Builds the context, for a test that needs a subclass of <see cref="SqliteCoreDbContext"/>.</param>
    public SqliteCoreDatabase(Func<DbContextOptions<CoreDbContext>, SqliteCoreDbContext> createContext, params IInterceptor[] interceptors)
    {
        _connection = new SqliteConnection(IN_MEMORY);
        _connection.Open();
        lock (_copyLock)
        {
            _template.Value.BackupDatabase(_connection);
        }

        Context = createContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options);

        var services = new ServiceCollection();
        _ = services.AddScoped(typeof(ICoreRepository<>), typeof(CoreRepositoryWrapper<>));
        _ = services.AddScoped<CoreDbContext>(_ => Context);
        _ = services.AddScoped<ICoreWorkflowRepository, CoreWorkflowRepository>();
        _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();
        _ = services.AddScoped<IToolsSettingsRepository, ToolsSettingsRepository>();
        _repositories = services.BuildServiceProvider();

        UnitOfWork = new CoreUnitOfWork(Context, _repositories);
    }

    public SqliteCoreDbContext Context { get; }

    public ICoreUnitOfWork UnitOfWork { get; }

    public void Dispose()
    {
        Context.Dispose();
        _repositories.Dispose();
        _connection.Dispose();
    }

    private static SqliteConnection CreateTemplate()
    {
        var template = new SqliteConnection(IN_MEMORY);
        template.Open();

        using var context = new SqliteCoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseSqlite(template)
            .Options);
        _ = context.Database.EnsureCreated();

        return template;
    }
}
