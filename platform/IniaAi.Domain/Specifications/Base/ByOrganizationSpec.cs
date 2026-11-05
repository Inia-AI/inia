using Ardalis.Specification;
using IniaAi.Domain.Abstractions;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Base specification for filtering entities by organization ID.
/// Use this as a foundation for organization-scoped queries.
/// </summary>
/// <typeparam name="T">The entity type that has an OrganizationId property</typeparam>
public class ByOrganizationSpec<T> : Specification<T> where T : class, IOrganizationScoped
{
    public ByOrganizationSpec(string organizationId)
    {
        OrganizationId = organizationId;
        _ = Query.Where(entity => entity.OrganizationId == organizationId);
    }

    protected string OrganizationId { get; }
}