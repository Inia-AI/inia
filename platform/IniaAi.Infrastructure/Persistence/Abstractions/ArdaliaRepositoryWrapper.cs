using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Infrastructure.Persistence.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;
using System.Reflection;

namespace IniaAi.Infrastructure.Persistence.Abstractions;

/// <summary>
/// Wrapper around Ardalis.Specification RepositoryBase to implement our IRepository interface
/// This allows gradual migration to Ardalis.Specification while maintaining existing contracts
/// </summary>
public class ArdaliaRepositoryWrapper<T, TContext> : Ardalis.Specification.EntityFrameworkCore.RepositoryBase<T>, IRepository<T>
    where T : class
    where TContext : DbContext
{
    public ArdaliaRepositoryWrapper(TContext dbContext) : base(dbContext)
    {
    }

    // IRepository<T> methods that delegate to Ardalis RepositoryBase
    public async Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        return await base.GetByIdAsync(id, cancellationToken);
    }

    public async Task<T?> SingleOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        string? includeProperties = null,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = DbContext.Set<T>().Where(predicate);

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

    public async Task<T?> GetFirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await base.FirstOrDefaultAsync(specification, cancellationToken);
    }

    public async Task<TResult?> GetFirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await base.FirstOrDefaultAsync(specification, cancellationToken);
    }

    public async Task<IEnumerable<T>> FindAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await base.ListAsync(specification, cancellationToken);
    }

    public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await DbContext.Set<T>().Where(predicate).ToListAsync();
    }

    public async Task AddAsync(T entity)
    {
        _ = await base.AddAsync(entity);
    }

    public void Add(T entity)
    {
        _ = DbContext.Set<T>().Add(entity);
    }

    public void Remove(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is IAuditableEntity auditableEntity)
        {
            auditableEntity.IsDeleted = true;
            auditableEntity.DeletedAt = DateTime.UtcNow;
            _ = DbContext.Set<T>().Update(entity);
        }
        else
        {
            _ = DbContext.Set<T>().Remove(entity);
        }
    }

    public void RemoveRange(IEnumerable<T> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (typeof(IAuditableEntity).IsAssignableFrom(typeof(T)))
        {
            foreach (T entity in entities)
            {
                if (entity is IAuditableEntity auditableEntity)
                {
                    auditableEntity.IsDeleted = true;
                    auditableEntity.DeletedAt = DateTime.UtcNow;
                    _ = DbContext.Set<T>().Update(entity);
                }
            }
        }
        else
        {
            DbContext.Set<T>().RemoveRange(entities);
        }
    }

    public void HardRemove(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        _ = DbContext.Set<T>().Remove(entity);
    }

    public void HardRemoveRange(IEnumerable<T> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        DbContext.Set<T>().RemoveRange(entities);
    }

    public async Task RemoveById<TKey>(TKey id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (typeof(IAuditableEntity).IsAssignableFrom(typeof(T)))
        {
            // Use reflection to call the helper with correct generic args
            MethodInfo method = typeof(SoftDeleteHelper)
                .GetMethod(nameof(SoftDeleteHelper.MarkEntitySoftDeleted))!
                .MakeGenericMethod(typeof(T), typeof(TKey));

            _ = method.Invoke(null, parameters: [DbContext, id!, null!, null!]);

            return;
        }

        // Fallback to loading the entity
        T entity = await DbContext.Set<T>().FindAsync([id!], cancellationToken) ?? throw new InvalidOperationException($"Entity of type {typeof(T).Name} with id '{id}' was not found.");

        Remove(entity);
    }

    public async Task RemoveByIds<TKey>(IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        // Fetch entities matching the provided ids
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return;
        }

        // Get entity type key property (assumes a single primary key named "Id")
        IEntityType? entityType = DbContext.Model.FindEntityType(typeof(T));
        IProperty? keyProp = (entityType?.FindPrimaryKey()?.Properties.FirstOrDefault())
            ?? throw new InvalidOperationException($"Unable to find the primary key for type {typeof(T).Name}");

        // Build a lambda: x => idList.Contains(x.Id)
        ParameterExpression param = Expression.Parameter(typeof(T), "x");
        MemberExpression property = Expression.Property(param, keyProp.Name);
        MethodInfo? containsMethod = typeof(List<TKey>).GetMethod("Contains", [typeof(TKey)]);
        MethodCallExpression containsCall = Expression.Call(Expression.Constant(idList), containsMethod!, property);
        var lambda = Expression.Lambda<Func<T, bool>>(containsCall, param);

        List<T> entities = await DbContext.Set<T>().Where(lambda).ToListAsync(cancellationToken);

        if (typeof(IAuditableEntity).IsAssignableFrom(typeof(T)))
        {
            foreach (T? entity in entities)
            {
                if (entity is IAuditableEntity auditableEntity)
                {
                    auditableEntity.IsDeleted = true;
                    auditableEntity.DeletedAt = DateTime.UtcNow;
                    _ = DbContext.Set<T>().Update(entity);
                }
            }
        }
        else
        {
            DbContext.Set<T>().RemoveRange(entities);
        }
    }

    public IQueryable<T> GetAll()
    {
        return DbContext.Set<T>();
    }

    public IQueryable<T> GetAll(ISpecification<T> specification)
    {
        return DbContext.Set<T>().WithSpecification(specification);
    }

    public async Task<IList<T>> GetAllAsync()
    {
        return await DbContext.Set<T>().ToListAsync();
    }

    public async Task<IList<T>> GetAllAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        return await base.ListAsync(specification, cancellationToken);
    }

    public async Task<IList<TResult>> GetAllAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        return await base.ListAsync(specification, cancellationToken);
    }
}
