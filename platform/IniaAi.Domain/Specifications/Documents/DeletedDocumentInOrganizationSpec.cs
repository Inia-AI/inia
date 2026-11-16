using Ardalis.Specification;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Specifications.Documents;

/// <summary>
/// The document, when it belongs to the organization and was deleted. Query filters are ignored,
/// because they hide a deleted document.
/// </summary>
public sealed class DeletedDocumentInOrganizationSpec : Specification<Document>
{
    public DeletedDocumentInOrganizationSpec(string documentId, string organizationId)
    {
        _ = Query
            .IgnoreQueryFilters()
            .Where(document => document.Id == documentId && document.OrganizationId == organizationId && document.IsDeleted)
            .AsNoTracking();
    }
}
