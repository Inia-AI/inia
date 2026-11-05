using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.Documents.ListDocumentPages;

/// <summary>
/// Query for retrieving a paginated, OData-filterable list of pages for a
/// specific document. The parent document is used to enforce organization
/// scoping — the caller must be able to read the document before its pages are
/// returned. A search keeps the page with that number and the pages whose text
/// or overview contains it.
/// </summary>
public record PaginatedResponseDocumentPagesQuery : IRequest<ErrorOr<ODataPaginatedResponse<PageSummaryDto>>>
{
    public const string SEARCH_PARAMETER = "term";

    public PaginatedResponseDocumentPagesQuery(
        string organizationId,
        string userId,
        string documentId,
        ODataQueryOptions<PageSummaryDto> queryOptions,
        string? search = null)
    {
        OrganizationId = organizationId;
        UserId = userId;
        DocumentId = documentId;
        QueryOptions = queryOptions;
        Search = search;
    }

    public string OrganizationId { get; }
    public string UserId { get; }
    public string DocumentId { get; }
    public ODataQueryOptions<PageSummaryDto> QueryOptions { get; set; }

    public string? Search { get; }
}
