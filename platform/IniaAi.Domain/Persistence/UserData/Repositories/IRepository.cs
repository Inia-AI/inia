using Ardalis.Specification;
using System.Linq.Expressions;

namespace IniaAi.Domain.Persistence.UserData.Repositories;

public interface IRepository<T>
{
    /// <summary>
    /// Get an entity from the repository.
    /// </summary>
    /// <param name="id">The id of the entity.</param>
    /// <returns>The entity if found, otherwise null.</returns>
    Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default);
    Task<T?> SingleOrDefaultAsync(
       Expression<Func<T, bool>> predicate,
       string? includeProperties = null,
       bool asNoTracking = false,
       CancellationToken cancellationToken = default);

    Task<T?> GetFirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    Task<TResult?> GetFirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find entities matching a specification.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <returns>A list of entities matching the specification.</returns>
    Task<IEnumerable<T>> FindAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find entities matching a predicate.
    /// </summary>
    /// <param name="predicate">The predicate to match.</param>
    /// <returns>A list of entities matching the predicate.</returns>
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);

    /// <summary>
    /// Add an entity to the repository. Whether it is saved at once is implementation-defined:
    /// the Ardalis-backed repositories save per call, the in-house ones write on the unit of
    /// work's next <c>CompleteAsync</c>. Use <see cref="Add"/> when the insert must ride that commit.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <returns></returns>
    Task AddAsync(T entity);

    /// <summary>
    /// Track an entity for insertion without saving; it is written by the unit of work's
    /// next <c>CompleteAsync</c>, in the same transaction as every other pending change.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    void Add(T entity);

    /// <summary>
    /// Remove an entity from the repository. If entity supports soft delete,
    /// it will be soft deleted.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    void Remove(T entity);

    /// <summary>
    /// Remove the entities from the repository. If entity supports soft delete,
    /// they will be soft deleted.
    /// </summary>
    /// <param name="entities">The entities to remove.</param>
    void RemoveRange(IEnumerable<T> entities);

    /// <summary>
    /// Remove an entity from the repository permanently.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    void HardRemove(T entity);

    /// <summary>
    /// Remove entities from the repository permanently.
    /// </summary>
    /// <param name="entities">The entities to remove.</param>
    void HardRemoveRange(IEnumerable<T> entities);

    /// <summary>
    /// Remove an entity by id from the repository.
    /// </summary>
    /// <param name="id">The entity id to remove.</param>
    Task RemoveById<TKey>(TKey id, CancellationToken cancellationToken = default);

    Task RemoveByIds<TKey>(IEnumerable<TKey> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all entities. This method is not recommended
    /// </summary>
    /// <returns>The <see cref="IQueryable{T}"/>.</returns>
    IQueryable<T> GetAll();

    /// <summary>
    /// Gets all entities matching a specification. This method is not recommended for large datasets.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <returns>An <see cref="IQueryable{T}"/> that contains elements that satisfy the specification.</returns>
    IQueryable<T> GetAll(ISpecification<T> specification);

    /// <summary>
    /// Gets all entities. This method is not recommended
    /// </summary>
    /// <returns>The <see cref="IQueryable{T}"/>.</returns>
    Task<IList<T>> GetAllAsync();

    /// <summary>
    /// Gets all entities matching a specification asynchronously. This method is not recommended for large datasets.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of entities that satisfy the specification.</returns>
    Task<IList<T>> GetAllAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all entities using a specification with projection asynchronously. This method is not recommended for large datasets.
    /// </summary>
    /// <param name="specification">The specification to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of projected entities that satisfy the specification.</returns>
    Task<IList<TResult>> GetAllAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);
}
