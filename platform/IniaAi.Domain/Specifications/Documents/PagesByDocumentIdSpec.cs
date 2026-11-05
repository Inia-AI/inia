using Ardalis.Specification;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Specifications.Documents;

/// <summary>
/// Specification for retrieving pages belonging to a single document, ordered by
/// logical page number. Includes <see cref="Page.EmbeddingVector"/> so the list
/// endpoint can report whether an embedding has been generated without a second
/// round trip. A search keeps the page with that number and the pages whose text
/// or overview contains it, ignoring case. Read-only.
/// </summary>
public sealed class PagesByDocumentIdSpec : Specification<Page>
{
    public PagesByDocumentIdSpec(string documentId, string? search = null)
    {
        _ = Query.Where(p => p.DocumentId == documentId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLowerInvariant();
            int pageNumber = int.TryParse(term, out int parsed) ? parsed : -1;
            _ = Query.Where(p => p.PageNumber == pageNumber
                || (p.Text != null && p.Text.ToLower().Contains(term))
                || (p.Overview != null && p.Overview.ToLower().Contains(term)));
        }

        _ = Query
            .Include(p => p.EmbeddingVector)
            .OrderBy(p => p.PageNumber)
            .AsNoTracking();
    }
}
