using Ardalis.Specification;
using IniaAi.Domain.Entities.SharedComponents;

namespace IniaAi.Domain.Specifications.SharedComponents;

public sealed class SharedComponentsListSpec : Specification<SharedComponent>
{
    public SharedComponentsListSpec()
    {
        _ = Query
            .AsNoTracking()
            .OrderBy(c => c.Name);
    }
}
