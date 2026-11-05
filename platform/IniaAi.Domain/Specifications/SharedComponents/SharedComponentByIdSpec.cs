using Ardalis.Specification;
using IniaAi.Domain.Entities.SharedComponents;

namespace IniaAi.Domain.Specifications.SharedComponents;

public sealed class SharedComponentByIdSpec
    : Specification<SharedComponent>, ISingleResultSpecification<SharedComponent>
{
    public SharedComponentByIdSpec(string sharedComponentId)
    {
        _ = Query
            .Where(c => c.Id == sharedComponentId)
            .AsNoTracking();
    }
}
