using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Documents;
using IniaAi.Application.Errors;
using IniaAi.Application.Queries.Documents;
using IniaAi.Common.Extensions;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Documents.ProcessDocument;

public class ProcessDocumentCommandHandler : IRequestHandler<ProcessDocumentCommand, ErrorOr<DocumentSummaryDto>>
{
    private readonly ILogger<ProcessDocumentCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userData;
    private readonly IWorkQueue<DocumentProcessingRequest> _queue;

    public ProcessDocumentCommandHandler(
        ILogger<ProcessDocumentCommandHandler> logger,
        IUserDataUnitOfWork userData,
        IWorkQueue<DocumentProcessingRequest> queue)
    {
        _logger = logger;
        _userData = userData;
        _queue = queue;
    }

    public async Task<ErrorOr<DocumentSummaryDto>> Handle(ProcessDocumentCommand request, CancellationToken cancellationToken)
    {
        Document? document = await _userData.Documents.GetFirstOrDefaultAsync(new DocumentByIdTrackingSpec(request.DocumentId), cancellationToken);
        if (document is null)
        {
            return DocumentErrorFactory.NotFound();
        }

        if (document.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning(
                "User {UserId} in organization {OrganizationId} attempted to process document {DocumentId} owned by {OwnerOrganizationId}",
                request.UserId,
                request.OrganizationId,
                LogSanitizer.ForLog(request.DocumentId),
                document.OrganizationId);
            return DocumentErrorFactory.NotFound();
        }

        if (!document.MediaType.ProducesPages())
        {
            return DocumentErrorFactory.NotProcessable(document.MediaType.ToString());
        }

        if (document.ProcessingState is DocumentProcessingState.Queued)
        {
            return DocumentErrorFactory.ProcessingAlreadyQueued();
        }

        try
        {
            await DocumentProcessingStarter.StartAsync(_userData, _queue, document, request.UserId, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return DocumentErrorFactory.ProcessingAlreadyQueued();
        }

        _logger.LogInformation("Standalone processing queued for document {DocumentId} by user {UserId}", document.Id, request.UserId);

        return DocumentProjections.SummaryOf(document);
    }
}
