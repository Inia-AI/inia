using Ardalis.Specification;
using IniaAi.Domain.Entities.SharedComponents;

namespace IniaAi.Domain.Specifications.SharedComponents;

public sealed class SharedComponentCategoriesSpec : Specification<SharedComponentCategory>
{
    public SharedComponentCategoriesSpec()
    {
        _ = Query
            .AsNoTracking()
            .OrderBy(c => c.DisplayOrder == null)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name);
    }
}
