using Ardalis.Specification;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Specifications.Documents;

/// <summary>
/// The text of the organization's pages with the given ids, for the pages a downloaded run's
/// citation traces point at. No tracking.
/// </summary>
public sealed class CitationTracePagesSpec : Specification<Page, CitationTracePage>
{
    public CitationTracePagesSpec(string organizationId, IReadOnlyCollection<string> pageIds)
    {
        _ = Query
            .Where(p => pageIds.Contains(p.Id) && p.Document!.OrganizationId == organizationId)
            .AsNoTracking();

        Query.Select(p => new CitationTracePage(p.Id, p.Text ?? p.RawText ?? string.Empty));
    }
}
