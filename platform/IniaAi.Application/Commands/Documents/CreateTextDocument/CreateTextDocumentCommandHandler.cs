using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.UploadLimits;
using IniaAi.Application.Documents;
using IniaAi.Application.Queries.Documents;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Documents;
using Microsoft.Extensions.Logging;
using System.Text;

namespace IniaAi.Application.Commands.Documents.CreateTextDocument;

public class CreateTextDocumentCommandHandler : IRequestHandler<CreateTextDocumentCommand, ErrorOr<DocumentSummaryDto>>
{
    private const string PASTED_TEXT_SOURCE = "Pasted text";

    private readonly ILogger<CreateTextDocumentCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userData;
    private readonly IFileStorageManager _storage;
    private readonly IWorkQueue<DocumentProcessingRequest> _queue;
    private readonly IUploadLimits _uploadLimits;

    public CreateTextDocumentCommandHandler(
        ILogger<CreateTextDocumentCommandHandler> logger,
        IUserDataUnitOfWork userData,
        IFileStorageManager storage,
        IWorkQueue<DocumentProcessingRequest> queue,
        IUploadLimits uploadLimits)
    {
        _logger = logger;
        _userData = userData;
        _storage = storage;
        _queue = queue;
        _uploadLimits = uploadLimits;
    }

    public async Task<ErrorOr<DocumentSummaryDto>> Handle(CreateTextDocumentCommand request, CancellationToken cancellationToken)
    {
        string fileName = TextDocuments.FileNameOf(request.Name, request.Format);

        Document? document = null;
        UploadRefusal? refusal = await _uploadLimits.StoreWithinLimitsAsync(
            new UploadLimitsRequest(request.OrganizationId, request.Uploader, Encoding.UTF8.GetByteCount(request.Text)),
            async ct =>
            {
                byte[] bytes = Encoding.UTF8.GetBytes(request.Text);
                document = await _userData.Documents.UploadNewFileCreateNewDocumentAsync(
                    bytes,
                    request.OrganizationId,
                    request.UserId,
                    fileName,
                    _storage.GetProviderForTenant(request.OrganizationId),
                    TextDocuments.MediaTypeOf(request.Format));
                document.Sources = [PASTED_TEXT_SOURCE];
                _ = await DocumentStorage.StoreBytesAsync(_storage, document, bytes);
                _ = await _userData.CompleteAsync(ct);
            },
            cancellationToken);

        if (refusal is not null)
        {
            return refusal.ToError();
        }

        if (document is null)
        {
            throw new InvalidOperationException("The text was within the upload limits, but no document was stored.");
        }

        await DocumentProcessingStarter.StartAsync(_userData, _queue, document, request.UserId, cancellationToken);

        _logger.LogInformation("Text document {DocumentId} ({Length} chars) created and queued for processing", document.Id, request.Text.Length);

        return DocumentProjections.SummaryOf(document);
    }
}
