using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Persistence.UserData.UnitOfWork;

public class UserDataUnitOfWork : IUserDataUnitOfWork
{
    private readonly UserDataDbContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, object> _genericRepositories = [];
    private bool _disposed;

    // Lazy-loaded specific repositories

    public UserDataUnitOfWork(UserDataDbContext context, IServiceProvider serviceProvider)
    {
        _context = context;
        _serviceProvider = serviceProvider;
    }

    // Lazy properties - created only when accessed
    public IDocumentRepository Documents =>
        field ??= _serviceProvider.GetRequiredService<IDocumentRepository>();

    public IFileRepository Files =>
        field ??= _serviceProvider.GetRequiredService<IFileRepository>();

    public IFileMetadataRepository FileMetadata =>
        field ??= _serviceProvider.GetRequiredService<IFileMetadataRepository>();

    public IUserDataWorkflowRepository Workflows =>
        field ??= _serviceProvider.GetRequiredService<IUserDataWorkflowRepository>();

    public IUserRepository Users =>
        field ??= _serviceProvider.GetRequiredService<IUserRepository>();

    public IOrganizationRepository Organizations =>
        field ??= _serviceProvider.GetRequiredService<IOrganizationRepository>();

    public IWorkflowExecutionRepository WorkflowExecutions =>
        field ??= _serviceProvider.GetRequiredService<IWorkflowExecutionRepository>();

    /// <summary>
    /// Generic repository method for new entities - avoids adding new properties to the interface.
    /// </summary>
    public IRepository<T> Repository<T>() where T : class
    {
        Type type = typeof(T);

        if (!_genericRepositories.ContainsKey(type))
        {
            _genericRepositories[type] = _serviceProvider.GetRequiredService<IUserDataRepository<T>>();
        }

        return (IRepository<T>)_genericRepositories[type];
    }

    public async Task<int> CompleteAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevertAsync<T>(T entity) where T : class
    {
        _context.Entry(entity).State = EntityState.Unchanged;
        await Task.CompletedTask;
    }

    public async Task UntrackAsync<T>(T entity) where T : class
    {
        _context.Entry(entity).State = EntityState.Detached;
        await Task.CompletedTask;
    }

    public async Task<bool> ReloadAsync<T>(T entity) where T : class
    {
        EntityEntry<T> entry = _context.Entry(entity);

        if (entry.State == EntityState.Detached)
        {
            DetachOtherInstanceWithSameKey(entry);
        }

        PropertyValues? databaseValues = await entry.GetDatabaseValuesAsync();

        if (databaseValues is null)
        {
            entry.State = EntityState.Detached;

            return false;
        }

        entry.CurrentValues.SetValues(databaseValues);
        entry.OriginalValues.SetValues(databaseValues);
        entry.State = EntityState.Unchanged;

        return true;
    }

    private void DetachOtherInstanceWithSameKey<T>(EntityEntry<T> entry) where T : class
    {
        IKey? primaryKey = entry.Metadata.FindPrimaryKey();

        if (primaryKey is null)
        {
            return;
        }

        object?[] keyValues = primaryKey.Properties.Select(property => entry.Property(property.Name).CurrentValue).ToArray();

        EntityEntry<T>? other = _context.ChangeTracker.Entries<T>().FirstOrDefault(candidate =>
            !ReferenceEquals(candidate.Entity, entry.Entity)
            && primaryKey.Properties.Select(property => candidate.Property(property.Name).CurrentValue).SequenceEqual(keyValues));

        if (other is not null)
        {
            other.State = EntityState.Detached;
        }
    }

    public void ClearChangeTracker()
    {
        _context.ChangeTracker.Clear();
    }

    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> attempt,
        Func<T, bool> commits,
        CancellationToken cancellationToken = default)
    {
        List<object> trackedByAttempt = [];
        void Record(object? sender, EntityTrackedEventArgs args)
        {
            trackedByAttempt.Add(args.Entry.Entity);
        }

        _context.ChangeTracker.Tracked += Record;
        try
        {
            return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                bool committed = false;
                try
                {
                    await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync(token);
                    T result = await attempt(token);
                    if (commits(result))
                    {
                        await transaction.CommitAsync(token);
                        committed = true;
                    }

                    return result;
                }
                finally
                {
                    if (!committed)
                    {
                        Detach(trackedByAttempt);
                    }
                }
            }, cancellationToken);
        }
        finally
        {
            _context.ChangeTracker.Tracked -= Record;
        }
    }

    private void Detach(List<object> entities)
    {
        foreach (object entity in entities)
        {
            _context.Entry(entity).State = EntityState.Detached;
        }

        entities.Clear();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _context?.Dispose();
        }

        _disposed = true;
    }
}
