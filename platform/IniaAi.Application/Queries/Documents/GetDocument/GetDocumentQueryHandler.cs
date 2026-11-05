using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Specifications.Common;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace IniaAi.Application.Queries.Documents.GetDocument;

/// <summary>
/// Handler for <see cref="GetDocumentQuery"/>. Projects the document to
/// <see cref="DocumentDetailDto"/> in one query, with its organization beside it
/// for the explicit org check. Page count is a <c>COUNT</c> subquery so the
/// detail payload stays bounded even for very long documents.
/// </summary>
public class GetDocumentQueryHandler : IRequestHandler<GetDocumentQuery, ErrorOr<DocumentDetailDto>>
{
    private static readonly Expression<Func<Document, OwnedDetail>> _toOwnedDetail = ProjectionExpressions.Of<Document, OwnedDetail>(
        document => new OwnedDetail(document.OrganizationId, DocumentProjections.ToDetail.Inline(document)));

    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<GetDocumentQueryHandler> _logger;

    public GetDocumentQueryHandler(
        IUserDataUnitOfWork unitOfWork,
        ILogger<GetDocumentQueryHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ErrorOr<DocumentDetailDto>> Handle(GetDocumentQuery request, CancellationToken cancellationToken)
    {
        OwnedDetail? owned = await _unitOfWork.Documents.GetAll()
            .Where(d => d.Id == request.DocumentId)
            .Select(_toOwnedDetail)
            .FirstOrDefaultAsync(cancellationToken);

        if (owned is null)
        {
            _logger.LogWarning(
                "User {UserId} requested document {DocumentId} that does not exist",
                request.UserId,
                LogSanitizer.ForLog(request.DocumentId));
            return DocumentErrorFactory.NotFound();
        }

        if (owned.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning(
                "User {UserId} in organization {OrganizationId} attempted to read document {DocumentId} owned by {OwnerOrganizationId}",
                request.UserId,
                request.OrganizationId,
                owned.Detail.Id,
                owned.OrganizationId);
            return DocumentErrorFactory.NotFound();
        }

        return owned.Detail;
    }

    private sealed record OwnedDetail(string OrganizationId, DocumentDetailDto Detail);
}
