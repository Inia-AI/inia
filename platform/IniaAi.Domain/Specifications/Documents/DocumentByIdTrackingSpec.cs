using Ardalis.Specification;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Specifications.Documents;

/// <summary>
/// Specification for retrieving a single <see cref="Document"/> by id with change
/// tracking enabled. Used by update and delete handlers where the entity must
/// remain attached to the context so EF can persist mutations and cascade to
/// <see cref="FileMetadata"/>.
/// </summary>
public sealed class DocumentByIdTrackingSpec : Specification<Document>, ISingleResultSpecification<Document>
{
    public DocumentByIdTrackingSpec(string id)
    {
        _ = Query
            .Where(d => d.Id == id)
            .Include(d => d.FileMetadata);
    }
}
