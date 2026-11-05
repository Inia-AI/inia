using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows.Templates;

namespace IniaAi.Domain.Specifications.DocumentCategories;

/// <summary>
/// Specification for retrieving a DocumentCategory by its ID.
/// Read-only with AsNoTracking for query performance.
/// </summary>
public sealed class DocumentCategoryByIdSpec : Specification<DocumentCategory>, ISingleResultSpecification<DocumentCategory>
{
    public DocumentCategoryByIdSpec(string id)
    {
        _ = Query
            .Where(dc => dc.Id == id)
            .AsNoTracking();
    }
}
