using IniaAi.Domain.Persistence.UserData.Repositories;

namespace IniaAi.Domain.UserData.Interfaces;

/// <summary>
/// Represents a unit of work for user data. This is used to manage the
/// repositories for the user data context.
/// <para>
/// The unit of work is added to the
/// dependency injection container as a scoped service. This means that a new
/// instance of the unit of work is created for each HTTP request or background
/// task.
/// </para>
/// </summary>
public interface IUserDataUnitOfWork : IDisposable
{
    IUserRepository Users { get; }
    IDocumentRepository Documents { get; }
    IFileRepository Files { get; } // For blob storage only (Id + Bytes)
    IFileMetadataRepository FileMetadata { get; } // For file metadata
    IOrganizationRepository Organizations { get; }
    IUserDataWorkflowRepository Workflows { get; }
    IWorkflowExecutionRepository WorkflowExecutions { get; }

    /// <summary>
    /// Gets a generic repository for any entity type. Use this for new entities
    /// instead of adding specific repository properties to avoid constructor bloat.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    /// <returns>Generic repository for the specified entity type</returns>
    IRepository<T> Repository<T>() where T : class;

    Task<int> CompleteAsync(CancellationToken cancellationToken = default);
    Task RevertAsync<T>(T entity) where T : class;
    Task UntrackAsync<T>(T entity) where T : class;

    /// <summary>
    /// Overwrites an entity's values, including its concurrency token, with the row as it currently
    /// exists in the database and leaves the entity tracked as unchanged; a detached entity replaces
    /// any other tracked instance of the same key. Returns <c>false</c>, leaving the entity
    /// untracked, when the row no longer exists.
    /// </summary>
    Task<bool> ReloadAsync<T>(T entity) where T : class;

    /// <summary>
    /// Detaches every tracked entity from the underlying context's change tracker. Used after the
    /// executed-workflow skeleton is inserted so the shared context tracks nothing during the run;
    /// each block then persists its own result through its own pooled context
    /// (<c>WorkflowBlockPersistence</c>), removing the DetectChanges-vs-mutation race that parallel
    /// block execution would otherwise hit on the shared graph.
    /// </summary>
    void ClearChangeTracker();

    /// <summary>
    /// Runs <paramref name="attempt"/> inside one transaction under the provider's execution strategy,
    /// so a transient failure retries the whole unit. The transaction commits when
    /// <paramref name="commits"/> accepts the attempt's result and rolls back otherwise. A rolled-back,
    /// failed or retried attempt detaches every entity it started tracking, so nothing it added can be
    /// saved later. It cannot undo a change to an entity tracked before the call: save pending changes
    /// first.
    /// </summary>
    Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> attempt,
        Func<T, bool> commits,
        CancellationToken cancellationToken = default);
}
