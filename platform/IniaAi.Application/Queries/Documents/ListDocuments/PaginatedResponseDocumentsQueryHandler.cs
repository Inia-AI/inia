using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.OData;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;

namespace IniaAi.Application.Queries.Documents.ListDocuments;

/// <summary>
/// Handler for <see cref="PaginatedResponseDocumentsQuery"/>. Builds an
/// org-scoped <see cref="IQueryable{Document}"/>, projects it to
/// <see cref="DocumentSummaryDto"/> and applies the request's
/// $filter/$orderby/$top/$skip to that projection. This pushes filtering and
/// paging down to the database rather than loading the full tenant corpus into
/// memory.
/// </summary>
public class PaginatedResponseDocumentsQueryHandler
    : IRequestHandler<PaginatedResponseDocumentsQuery, ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;

    public PaginatedResponseDocumentsQueryHandler(
        IUserDataUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<ODataPaginatedResponse<DocumentSummaryDto>>> Handle(
        PaginatedResponseDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        IQueryable<Document> baseQuery = _unitOfWork.Documents
            .GetAll()
            .Where(d => d.OrganizationId == request.OrganizationId);

        if (!string.IsNullOrWhiteSpace(request.JobId))
        {
            // Org isolation is preserved because the outer Document query is already scoped to
            // the caller's tenant — any DocumentInfo pointing at a cross-tenant Document is
            // dropped by the OrganizationId filter above. (DocumentInfo itself has no
            // OrganizationId column.)
            IQueryable<DocumentInfo> runInputQuery = _unitOfWork.Repository<DocumentInfo>()
                .GetAll()
                .Where(DocumentInfo.IsRunInputOf(request.JobId));
            baseQuery = baseQuery.Where(d => runInputQuery.Any(di => di.DocumentId == d.Id));
        }

        return await baseQuery.Select(DocumentProjections.ToSummary).ToODataPageAsync(request.QueryOptions, cancellationToken);
    }
}
