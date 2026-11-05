using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Infrastructure.Persistence.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;
using System.Reflection;

namespace IniaAi.Infrastructure.Persistence.Abstractions;

public abstract class RepositoryBase<T> where T : class
{
    protected readonly DbContext Context;
    protected readonly DbSet<T> Entities;

    internal RepositoryBase(DbContext context)
    {
        Context = context;
        Entities = Context.Set<T>();
    }

    public virtual async Task AddAsync(T entity)
    {
        _ = await Entities.AddAsync(entity);
    }

    public virtual void Add(T entity)
    {
        _ = Entities.Add(entity);
    }

    public virtual async Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        return await Entities.FindAsync([id], cancellationToken: cancellationToken);
    }

    public async Task<T?> SingleOrDefaultAsync(
       Expression<Func<T, bool>> predicate,
       string? includeProperties = null,
       bool asNoTracking = false,
       CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = Entities.Where(predicate);

        if (!string.IsNullOrWhiteSpace(includeProperties))
        {
            foreach (string includeProp in includeProperties.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Include(includeProp.Trim());
            }
        }

        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    public virtual void Remove(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is IAuditableEntity auditableEntity)
        {
            SoftDelete(entity, auditableEntity);
        }
        else
        {
            _ = Entities.Remove(entity);
        }
    }

    public virtual void RemoveRange(IEnumerable<T> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (typeof(IAuditableEntity).IsAssignableFrom(typeof(T)))
        {
            foreach (T entity in entities)
            {
                if (entity is IAuditableEntity auditableEntity)
                {
                    SoftDelete(entity, auditableEntity);
                }
            }
        }
        else
        {
            Entities.RemoveRange(entities);
        }
    }

    private void SoftDelete(T entity, IAuditableEntity auditableEntity)
    {
        auditableEntity.IsDeleted = true;
        auditableEntity.DeletedAt = DateTime.UtcNow;

        // Update() would turn an entity that is not saved yet into an UPDATE of a row that does not exist.
        if (Context.Entry(entity).State is not EntityState.Added)
        {
            _ = Entities.Update(entity);
        }
    }

    public virtual void HardRemove(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        _ = Entities.Remove(entity);
    }

    public virtual void HardRemoveRange(IEnumerable<T> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        Entities.RemoveRange(entities);
    }

    /// <summary>
    /// Gets the first or default entity based on a specification
    /// </summary>
    /// <param name="specification">The specification to apply</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The first entity that matches the specification or default</returns>
    public virtual async Task<T?> GetFirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await Entities.WithSpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the first or default projected entity based on a specification
    /// </summary>
    /// <param name="specification">The specification to apply</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The first projected entity that matches the specification or default</returns>
    public virtual async Task<TResult?> GetFirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await Entities.WithSpecification(specification).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Find entities matching a specification
    /// </summary>
    /// <param name="specification">The specification to match</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of entities matching the specification</returns>
    public virtual async Task<IEnumerable<T>> FindAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await Entities.WithSpecification(specification).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Find entities matching a predicate
    /// </summary>
    /// <param name="predicate">The predicate to match</param>
    /// <returns>A list of entities matching the predicate</returns>
    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await Entities.Where(predicate).ToListAsync();
    }

    public virtual async Task RemoveById<TKey>(TKey id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (typeof(IAuditableEntity).IsAssignableFrom(typeof(T)))
        {
            // Use reflection to call the helper with correct generic args
            MethodInfo method = typeof(SoftDeleteHelper)
                .GetMethod(nameof(SoftDeleteHelper.MarkEntitySoftDeleted))!
                .MakeGenericMethod(typeof(T), typeof(TKey));

            _ = method.Invoke(null, parameters: [Context, id!, null!, null!]);

            return;
        }

        // Fallback to loading the entity
        T entity = await Entities.FindAsync([id!], cancellationToken) ?? throw new InvalidOperationException($"Entity of type {typeof(T).Name} with id '{id}' was not found.");

        Remove(entity);
    }

    public virtual async Task RemoveByIds<TKey>(IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        // Fetch entities matching the provided ids
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return;
        }

        // Get entity type key property (assumes a single primary key named "Id")
        IEntityType? entityType = Context.Model.FindEntityType(typeof(T));
        IProperty? keyProp = (entityType?.FindPrimaryKey()?.Properties.FirstOrDefault())
            ?? throw new InvalidOperationException($"Unable to find the primary key for type {typeof(T).Name}");

        // Build a lambda: x => idList.Contains(x.Id)
        ParameterExpression param = Expression.Parameter(typeof(T), "x");
        MemberExpression property = Expression.Property(param, keyProp.Name);
        MethodInfo? containsMethod = typeof(List<TKey>).GetMethod("Contains", [typeof(TKey)]);
        MethodCallExpression containsCall = Expression.Call(Expression.Constant(idList), containsMethod!, property);
        var lambda = Expression.Lambda<Func<T, bool>>(containsCall, param);

        List<T> entities = await Entities.Where(lambda).ToListAsync(cancellationToken);

        if (typeof(IAuditableEntity).IsAssignableFrom(typeof(T)))
        {
            foreach (T? entity in entities)
            {
                if (entity is IAuditableEntity auditableEntity)
                {
                    SoftDelete(entity, auditableEntity);
                }
            }
        }
        else
        {
            Entities.RemoveRange(entities);
        }
    }

    /// <summary>
    /// Gets all entities. This method is not recommended
    /// </summary>
    /// <returns>The <see cref="IQueryable{T}"/>.</returns>
    public IQueryable<T> GetAll()
    {
        return Entities;
    }

    /// <summary>
    /// Gets all entities matching a specification. This method is not recommended for large datasets.
    /// </summary>
    /// <param name="specification">The specification to match</param>
    /// <returns>An <see cref="IQueryable{T}"/> that contains elements that satisfy the specification</returns>
    public IQueryable<T> GetAll(ISpecification<T> specification)
    {
        return Entities.WithSpecification(specification);
    }

    /// <summary>
    /// Gets all entities using a specification with projection. This method is not recommended for large datasets.
    /// </summary>
    /// <param name="specification">The specification to match</param>
    /// <returns>An <see cref="IQueryable{TResult}"/> that contains projected elements that satisfy the specification</returns>
    public IQueryable<TResult> GetAll<TResult>(ISpecification<T, TResult> specification)
    {
        return Entities.WithSpecification(specification);
    }

    /// <summary>
    /// Gets all entities. This method is not recommended
    /// </summary>
    /// <returns>The <see cref="IQueryable{T}"/>.</returns>
    public virtual async Task<IList<T>> GetAllAsync()
    {
        return await Entities.ToListAsync();
    }

    /// <summary>
    /// Gets all entities matching a specification asynchronously. This method is not recommended for large datasets.
    /// </summary>
    /// <param name="specification">The specification to match</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of entities that satisfy the specification</returns>
    public async Task<IList<T>> GetAllAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await Entities.WithSpecification(specification).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Gets all entities using a specification with projection asynchronously. This method is not recommended for large datasets.
    /// </summary>
    /// <param name="specification">The specification to match</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A list of projected entities that satisfy the specification</returns>
    public async Task<IList<TResult>> GetAllAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await Entities.WithSpecification(specification).ToListAsync(cancellationToken);
    }
}
