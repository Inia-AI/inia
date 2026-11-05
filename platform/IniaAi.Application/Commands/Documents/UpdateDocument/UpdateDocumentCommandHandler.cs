using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Errors;
using IniaAi.Application.Queries.Documents;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Documents.UpdateDocument;

/// <summary>
/// Handler for <see cref="UpdateDocumentCommand"/>. Loads the tracked document,
/// enforces the tenant check, then applies non-null/non-whitespace fields.
/// Returns the refreshed <see cref="DocumentDetailDto"/> so clients can update
/// their view without a follow-up GET.
/// </summary>
public class UpdateDocumentCommandHandler : IRequestHandler<UpdateDocumentCommand, ErrorOr<DocumentDetailDto>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<UpdateDocumentCommandHandler> _logger;

    public UpdateDocumentCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        ILogger<UpdateDocumentCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ErrorOr<DocumentDetailDto>> Handle(UpdateDocumentCommand request, CancellationToken cancellationToken)
    {
        Document? document = await _unitOfWork.Documents
            .GetFirstOrDefaultAsync(new DocumentByIdTrackingSpec(request.DocumentId), cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "User {UserId} attempted to update non-existent document {DocumentId}",
                request.UserId,
                LogSanitizer.ForLog(request.DocumentId));
            return DocumentErrorFactory.NotFound();
        }

        if (document.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning(
                "User {UserId} in organization {OrganizationId} attempted to update document {DocumentId} owned by {OwnerOrganizationId}",
                request.UserId,
                request.OrganizationId,
                document.Id,
                document.OrganizationId);
            return DocumentErrorFactory.NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            document.Name = ControlCharacters.ToSpaces(request.Name);
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            document.Description = request.Description;
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentCategoryId))
        {
            document.DocumentCategoryId = request.DocumentCategoryId;
        }

        if (!string.IsNullOrWhiteSpace(request.WorkflowTypeId))
        {
            document.WorkflowTypeId = request.WorkflowTypeId;
        }

        if (!string.IsNullOrWhiteSpace(request.Class))
        {
            document.Class = request.Class;
        }

        if (request.IsStatic == false)
        {
            document.WorkflowTypeId = null;
            document.Class = null;
        }

        if (!string.IsNullOrWhiteSpace(request.Topics))
        {
            document.Topics = request.Topics;
        }

        if (!string.IsNullOrWhiteSpace(request.TableOfContents))
        {
            document.TableOfContents = request.TableOfContents;
        }

        document.UpdatedAt = DateTime.UtcNow;
        document.UpdatedByUserId = request.UserId;

        _ = await _unitOfWork.CompleteAsync(cancellationToken);

        return await _unitOfWork.Documents.GetAll()
            .Where(d => d.Id == document.Id)
            .Select(DocumentProjections.ToDetail)
            .SingleAsync(cancellationToken);
    }
}
