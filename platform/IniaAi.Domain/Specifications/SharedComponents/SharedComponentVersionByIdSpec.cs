using Ardalis.Specification;
using IniaAi.Domain.Entities.SharedComponents;

namespace IniaAi.Domain.Specifications.SharedComponents;

public sealed class SharedComponentVersionByIdSpec
    : Specification<SharedComponentVersion>, ISingleResultSpecification<SharedComponentVersion>
{
    public SharedComponentVersionByIdSpec(string sharedComponentId, string versionId)
    {
        _ = Query
            .Where(v => v.SharedComponentId == sharedComponentId && v.Id == versionId)
            .AsNoTracking();
    }
}
