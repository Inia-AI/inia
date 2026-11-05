using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.OData;
using IniaAi.Application.Errors;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.Documents.ListDocumentPages;

/// <summary>
/// Handler for <see cref="PaginatedResponseDocumentPagesQuery"/>. First verifies
/// the parent document is visible to the caller (NotFound otherwise), then
/// returns an org-scoped, OData-filterable page list. This keeps the tenant
/// check at the document boundary and lets OData drive $filter/$orderby/$top on
/// page fields.
/// </summary>
public class PaginatedResponseDocumentPagesQueryHandler
    : IRequestHandler<PaginatedResponseDocumentPagesQuery, ErrorOr<ODataPaginatedResponse<PageSummaryDto>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<PaginatedResponseDocumentPagesQueryHandler> _logger;

    public PaginatedResponseDocumentPagesQueryHandler(
        IUserDataUnitOfWork unitOfWork,
        ILogger<PaginatedResponseDocumentPagesQueryHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ErrorOr<ODataPaginatedResponse<PageSummaryDto>>> Handle(
        PaginatedResponseDocumentPagesQuery request,
        CancellationToken cancellationToken)
    {
        var ownerInfo = await _unitOfWork.Documents
            .GetAll()
            .Where(d => d.Id == request.DocumentId)
            .Select(d => new { d.OrganizationId })
            .SingleOrDefaultAsync(cancellationToken);

        if (ownerInfo is null)
        {
            _logger.LogWarning(
                "User {UserId} requested pages for non-existent document {DocumentId}",
                request.UserId,
                LogSanitizer.ForLog(request.DocumentId));
            return DocumentErrorFactory.NotFound();
        }

        if (ownerInfo.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning(
                "User {UserId} in organization {OrganizationId} attempted to read pages of document {DocumentId} owned by {OwnerOrganizationId}",
                request.UserId,
                request.OrganizationId,
                LogSanitizer.ForLog(request.DocumentId),
                ownerInfo.OrganizationId);
            return DocumentErrorFactory.NotFound();
        }

        IQueryable<Page> baseQuery = _unitOfWork.Repository<Page>()
            .GetAll(new PagesByDocumentIdSpec(request.DocumentId, request.Search));

        return await baseQuery.Select(PageProjections.ToSummary).ToODataPageAsync(request.QueryOptions, cancellationToken);
    }
}
