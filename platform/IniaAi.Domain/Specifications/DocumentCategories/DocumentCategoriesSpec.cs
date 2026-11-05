using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows.Templates;

namespace IniaAi.Domain.Specifications.DocumentCategories;

/// <summary>
/// Specification for retrieving all DocumentCategories.
/// Read-only with AsNoTracking for query performance.
/// </summary>
public sealed class DocumentCategoriesSpec : Specification<DocumentCategory>
{
    public DocumentCategoriesSpec()
    {
        _ = Query.AsNoTracking();
    }
}
