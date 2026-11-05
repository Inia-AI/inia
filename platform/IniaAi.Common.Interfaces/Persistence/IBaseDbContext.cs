using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace IniaAi.Application.Common.Interfaces.Contexts;

public interface IBaseDbContext
{

    /// <summary>
    /// Persists all changes made in this context to the database.
    /// </summary>
    /// <returns>The number of state entries written to the database.</returns>
    int SaveChanges();

    /// <summary>
    /// Persists all changes asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of state entries written to the database asynchronously.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Attach<TEntity>(TEntity entity) where TEntity : class;
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    DbSet<TEntity> Set<TEntity>() where TEntity : class;

    EntityEntry<TEntity> Add<TEntity>(TEntity entity) where TEntity : class;
    EntityEntry<TEntity> Update<TEntity>(TEntity entity) where TEntity : class;
    EntityEntry<TEntity> Remove<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>
    /// Starts a new transaction against the underlying database.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the provider-configured execution strategy. Any user-initiated transaction must run
    /// inside <see cref="IExecutionStrategy.ExecuteAsync"/> so the whole unit retries together on
    /// transient failures.
    /// </summary>
    IExecutionStrategy CreateExecutionStrategy();

    /// <summary>
    /// Detaches every tracked entity. Call at the start of a retryable transactional unit so a
    /// retried attempt does not replay tracker state left behind by a rolled-back attempt.
    /// </summary>
    void ClearChangeTracker();
}
