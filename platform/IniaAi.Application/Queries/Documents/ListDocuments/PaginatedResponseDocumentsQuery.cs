using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.AspNetCore.OData.Query;

namespace IniaAi.Application.Queries.Documents.ListDocuments;

/// <summary>
/// Query for retrieving a paginated, OData-filterable list of documents scoped to
/// the caller's organization. Supports $filter, $orderby, $top, $skip, and $count.
/// When <paramref name="jobId"/> is supplied, the result set is pre-filtered to
/// documents that were processed under that workflow job (via
/// <c>DocumentInfo.JobId</c>).
/// </summary>
public record PaginatedResponseDocumentsQuery : IRequest<ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>>>
{
    public PaginatedResponseDocumentsQuery(
        string organizationId,
        string userId,
        ODataQueryOptions<DocumentSummaryDto> queryOptions,
        string? jobId = null)
    {
        OrganizationId = organizationId;
        UserId = userId;
        QueryOptions = queryOptions;
        JobId = jobId;
    }

    public string OrganizationId { get; }
    public string UserId { get; }
    public ODataQueryOptions<DocumentSummaryDto> QueryOptions { get; set; }
    public string? JobId { get; }
}
