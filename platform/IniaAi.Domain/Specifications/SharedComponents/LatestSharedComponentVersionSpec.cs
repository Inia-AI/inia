using Ardalis.Specification;
using IniaAi.Domain.Entities.SharedComponents;

namespace IniaAi.Domain.Specifications.SharedComponents;

public sealed class LatestSharedComponentVersionSpec
    : Specification<SharedComponentVersion>, ISingleResultSpecification<SharedComponentVersion>
{
    public LatestSharedComponentVersionSpec(string sharedComponentId)
    {
        _ = Query
            .Where(v => v.SharedComponentId == sharedComponentId)
            .OrderByDescending(v => v.SequenceNumber)
            .ThenByDescending(v => v.Id)
            .AsNoTracking();
    }
}
