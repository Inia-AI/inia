using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Common;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text;

namespace IniaAi.Application.Queries.Documents.GetDocumentText;

public class GetDocumentTextQueryHandler : IRequestHandler<GetDocumentTextQuery, ErrorOr<DocumentText>>
{
    private const string TEXT_EXTENSION = ".txt";

    private readonly IUserDataUnitOfWork _userData;
    private readonly ILogger<GetDocumentTextQueryHandler> _logger;

    public GetDocumentTextQueryHandler(IUserDataUnitOfWork userData, ILogger<GetDocumentTextQueryHandler> logger)
    {
        _userData = userData;
        _logger = logger;
    }

    public async Task<ErrorOr<DocumentText>> Handle(GetDocumentTextQuery request, CancellationToken cancellationToken)
    {
        var owner = await _userData.Documents.GetAll()
            .Where(d => d.Id == request.DocumentId)
            .Select(d => new { d.OrganizationId, d.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (owner is null)
        {
            return DocumentErrorFactory.NotFound();
        }

        if (owner.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning(
                "User {UserId} in organization {OrganizationId} attempted to read the text of document {DocumentId} owned by {OwnerOrganizationId}",
                request.UserId,
                request.OrganizationId,
                LogSanitizer.ForLog(request.DocumentId),
                owner.OrganizationId);
            return DocumentErrorFactory.NotFound();
        }

        var pages = await _userData.Repository<Page>().GetAll()
            .Where(p => p.DocumentId == request.DocumentId)
            .OrderBy(p => p.PageNumber)
            .Select(p => new { p.PageNumber, p.Text })
            .ToListAsync(cancellationToken);

        StringBuilder text = new();
        foreach (var page in pages)
        {
            _ = text.AppendLine($"<page{page.PageNumber}>");
            _ = text.AppendLine(page.Text);
            _ = text.AppendLine($"</page{page.PageNumber}>");
            _ = text.AppendLine();
        }

        return new DocumentText(owner.Name.TrimEnd().WithExtension(TEXT_EXTENSION), text.ToString());
    }
}
