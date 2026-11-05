using Ardalis.Specification;
using IniaAi.Domain.Abstractions;

namespace IniaAi.Domain.Specifications.Base;

/// <summary>
/// Base specification combining ID + organization filtering with read-only optimization.
/// Common pattern for secure, read-only entity retrieval within organization scope.
/// </summary>
/// <typeparam name="T">The entity type that has Id and OrganizationId properties</typeparam>
public class ReadOnlyByIdAndOrganizationSpec<T> : Specification<T> where T : class, IIdentifiable, IOrganizationScoped
{
    public ReadOnlyByIdAndOrganizationSpec(string id, string organizationId)
    {
        Id = id;
        OrganizationId = organizationId;
        _ = Query
            .Where(entity => entity.Id == id && entity.OrganizationId == organizationId)
            .AsNoTracking();
    }

    protected string Id { get; }
    protected string OrganizationId { get; }
}