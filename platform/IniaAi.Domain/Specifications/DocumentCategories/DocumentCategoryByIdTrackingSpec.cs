using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows.Templates;

namespace IniaAi.Domain.Specifications.DocumentCategories;

/// <summary>
/// Specification for retrieving a DocumentCategory by its ID with tracking enabled.
/// Used for update/delete operations where the entity needs to be tracked.
/// </summary>
public sealed class DocumentCategoryByIdTrackingSpec : Specification<DocumentCategory>, ISingleResultSpecification<DocumentCategory>
{
    public DocumentCategoryByIdTrackingSpec(string id)
    {
        _ = Query.Where(dc => dc.Id == id);
    }
}
