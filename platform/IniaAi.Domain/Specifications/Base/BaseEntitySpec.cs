using Ardalis.Specification;
using IniaAi.Domain.Abstractions;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Base specification for entities with ID and organization scope.
/// Provides reusable methods for common entity query patterns.
/// </summary>
/// <typeparam name="T">The entity type that implements IIdentifiable and IOrganizationScoped</typeparam>
public abstract class BaseEntitySpec<T> : Specification<T> where T : class, IIdentifiable, IOrganizationScoped
{
    /// <summary>
    /// Adds filtering by entity ID.
    /// </summary>
    protected void AddIdFilter(string id)
    {
        _ = Query.Where(entity => entity.Id == id);
    }

    /// <summary>
    /// Adds filtering by multiple entity IDs.
    /// </summary>
    protected void AddIdsFilter(IEnumerable<string> ids)
    {
        var idList = ids.ToList();
        _ = Query.Where(entity => idList.Contains(entity.Id));
    }

    /// <summary>
    /// Adds organization filtering to the query.
    /// </summary>
    protected void AddOrganizationFilter(string organizationId)
    {
        _ = Query.Where(entity => entity.OrganizationId == organizationId);
    }

    /// <summary>
    /// Adds combined ID and organization filtering (secure single entity access).
    /// </summary>
    protected void AddSecureEntityFilter(string id, string organizationId)
    {
        _ = Query.Where(entity => entity.Id == id && entity.OrganizationId == organizationId);
    }

    /// <summary>
    /// Adds read-only optimization (no change tracking).
    /// </summary>
    protected void AddReadOnlyOptimization()
    {
        _ = Query.AsNoTracking();
    }

    /// <summary>
    /// Adds ordering by ID.
    /// </summary>
    protected void AddIdOrdering()
    {
        _ = Query.OrderBy(entity => entity.Id);
    }

    /// <summary>
    /// Adds split query optimization for complex includes.
    /// </summary>
    protected void AddSplitQueryOptimization()
    {
        _ = Query.AsSplitQuery();
    }
}