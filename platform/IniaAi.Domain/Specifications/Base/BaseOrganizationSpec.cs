using Ardalis.Specification;
using IniaAi.Domain.Abstractions;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Base specification for organization-scoped entities with common query patterns.
/// Provides reusable methods for common filtering and optimization scenarios.
/// </summary>
/// <typeparam name="T">The entity type that implements IOrganizationScoped</typeparam>
public abstract class BaseOrganizationSpec<T> : Specification<T> where T : class, IOrganizationScoped
{
    /// <summary>
    /// Adds organization filtering to the query.
    /// </summary>
    protected void AddOrganizationFilter(string organizationId)
    {
        _ = Query.Where(entity => entity.OrganizationId == organizationId);
    }

    /// <summary>
    /// Adds read-only optimization (no change tracking).
    /// </summary>
    protected void AddReadOnlyOptimization()
    {
        _ = Query.AsNoTracking();
    }

    /// <summary>
    /// Adds split query optimization for complex includes.
    /// </summary>
    protected void AddSplitQueryOptimization()
    {
        _ = Query.AsSplitQuery();
    }

    /// <summary>
    /// Adds pagination support.
    /// </summary>
    protected void AddPagination(int skip, int take)
    {
        _ = Query.Skip(skip).Take(take);
    }

    /// <summary>
    /// Adds ordering by ID (common index pattern).
    /// </summary>
    protected void AddIdOrdering<TEntity>() where TEntity : class, T, IIdentifiable
    {
        _ = Query.OrderBy(entity => ((TEntity)entity).Id);
    }
}