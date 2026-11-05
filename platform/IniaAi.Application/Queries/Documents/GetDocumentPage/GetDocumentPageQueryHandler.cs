using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.Documents.GetDocumentPage;

public class GetDocumentPageQueryHandler : IRequestHandler<GetDocumentPageQuery, ErrorOr<PageDetailDto>>
{
    private readonly IUserDataUnitOfWork _userData;
    private readonly ILogger<GetDocumentPageQueryHandler> _logger;

    public GetDocumentPageQueryHandler(IUserDataUnitOfWork userData, ILogger<GetDocumentPageQueryHandler> logger)
    {
        _userData = userData;
        _logger = logger;
    }

    public async Task<ErrorOr<PageDetailDto>> Handle(GetDocumentPageQuery request, CancellationToken cancellationToken)
    {
        string? owner = await _userData.Documents.GetAll()
            .Where(d => d.Id == request.DocumentId)
            .Select(d => d.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);
        if (owner is null)
        {
            return DocumentErrorFactory.NotFound();
        }

        if (owner != request.OrganizationId)
        {
            _logger.LogWarning(
                "User {UserId} in organization {OrganizationId} attempted to read a page of document {DocumentId} owned by {OwnerOrganizationId}",
                request.UserId,
                request.OrganizationId,
                LogSanitizer.ForLog(request.DocumentId),
                owner);
            return DocumentErrorFactory.NotFound();
        }

        PageDetailDto? page = await _userData.Repository<Page>().GetAll()
            .Where(p => p.DocumentId == request.DocumentId && p.PageNumber == request.PageNumber)
            .Select(PageProjections.ToDetail)
            .FirstOrDefaultAsync(cancellationToken);
        if (page is null)
        {
            return DocumentErrorFactory.PageNotFound(request.PageNumber);
        }

        return page;
    }
}
