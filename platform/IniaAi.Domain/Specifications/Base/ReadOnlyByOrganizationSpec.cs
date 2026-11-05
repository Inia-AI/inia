using Ardalis.Specification;
using IniaAi.Domain.Abstractions;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Base specification combining organization filtering with read-only optimization.
/// Common pattern for read-only organization-scoped queries.
/// </summary>
/// <typeparam name="T">The entity type that has an OrganizationId property</typeparam>
public class ReadOnlyByOrganizationSpec<T> : Specification<T> where T : class, IOrganizationScoped
{
    public ReadOnlyByOrganizationSpec(string organizationId)
    {
        OrganizationId = organizationId;
        _ = Query
            .Where(entity => entity.OrganizationId == organizationId)
            .AsNoTracking();
    }

    protected string OrganizationId { get; }
}