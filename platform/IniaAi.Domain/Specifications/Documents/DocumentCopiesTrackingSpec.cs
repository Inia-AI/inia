using Ardalis.Specification;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Specifications.Documents;

/// <summary>
/// The tracked copies, with their <see cref="FileMetadata"/>, that an organization holds of the given documents, so a
/// delete of a document takes its copies with it.
/// </summary>
public sealed class DocumentCopiesTrackingSpec : Specification<Document>
{
    public DocumentCopiesTrackingSpec(IReadOnlyCollection<string> originalIds, string organizationId)
    {
        _ = Query
            .Where(d => d.MadeFromDocumentId != null && originalIds.Contains(d.MadeFromDocumentId) && d.OrganizationId == organizationId)
            .Include(d => d.FileMetadata);
    }
}
