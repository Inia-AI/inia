using Ardalis.Specification;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Specifications.Documents;

/// <summary>
/// Specification for retrieving multiple tracked <see cref="Document"/> entities by id set,
/// eager-loading <see cref="FileMetadata"/> so callers can mutate both sides of the
/// relationship without extra roundtrips. Used by bulk-delete handler to load all
/// candidates in a single query instead of N round-trips.
/// </summary>
public sealed class DocumentsByIdsTrackingSpec : Specification<Document>
{
    public DocumentsByIdsTrackingSpec(IReadOnlyCollection<string> ids)
    {
        _ = Query
            .Where(d => ids.Contains(d.Id))
            .Include(d => d.FileMetadata);
    }
}
