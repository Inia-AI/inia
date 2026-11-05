using Ardalis.Specification;
using IniaAi.Domain.Abstractions;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Performance-optimized base specification for organization filtering.
/// Includes query hints and optimizations for high-volume scenarios.
/// </summary>
/// <typeparam name="T">The entity type that has an OrganizationId property</typeparam>
public class OptimizedByOrganizationSpec<T> : Specification<T> where T : class, IOrganizationScoped
{
    public OptimizedByOrganizationSpec(string organizationId)
    {
        OrganizationId = organizationId;

        _ = Query
            .Where(entity => entity.OrganizationId == organizationId)
            .AsNoTracking()
            .AsSplitQuery(); // Optimize for complex includes
    }

    protected string OrganizationId { get; }
}

/// <summary>
/// Performance-optimized specification combining ID and organization filtering.
/// Uses compiled expressions and query optimization for frequently-accessed entities.
/// </summary>
/// <typeparam name="T">The entity type that has Id and OrganizationId properties</typeparam>
public class OptimizedByIdAndOrganizationSpec<T> : Specification<T> where T : class, IIdentifiable, IOrganizationScoped
{
    public OptimizedByIdAndOrganizationSpec(string id, string organizationId)
    {
        Id = id;
        OrganizationId = organizationId;

        _ = Query
            .Where(entity => entity.Id == id && entity.OrganizationId == organizationId)
            .AsNoTracking(); // Optimize for single entity retrieval
    }

    protected string Id { get; }
    protected string OrganizationId { get; }
}