using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queues;
using IniaAi.Common.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Documents;

/// <summary>
/// Runs one standalone processing request through the same pipeline an execution uses,
/// with no document classes and no workflow type: pages and their text. Without a workflow
/// there is no overview prompt, so the pages get no new overviews, and those an earlier run
/// wrote stay for the next run of that workflow to reuse.
/// Success is stamped by the pipeline itself (<c>ProcessedAt</c>); the attempt discipline —
/// skip a document no longer <c>Queued</c>, write a failure only onto the attempt that failed
/// — is the shared runner's, keyed on the request stamp.
/// </summary>
public class DocumentProcessingRunner : WorkRunner<DocumentProcessingRequest, Document>
{
    private const string NOT_STARTED_MESSAGE = "Processing was not started: the service stopped before this document was reached. Request processing again.";
    private const string INTERRUPTED_MESSAGE = "Processing was interrupted by a service stop. Request processing again.";
    private const string NOT_MARKED_PROCESSED_MESSAGE = "The processing pipeline returned without marking this document processed.";

    private readonly IDocumentProcessingService _processing;

    public DocumentProcessingRunner(
        ILogger<DocumentProcessingRunner> logger,
        IUserDataUnitOfWork userData,
        IDocumentProcessingService processing)
        : base(logger, userData)
    {
        _processing = processing;
    }

    protected override string InterruptedMessage => INTERRUPTED_MESSAGE;

    protected override string NotStartedMessage => NOT_STARTED_MESSAGE;

    protected override async Task<Document?> LoadAsync(DocumentProcessingRequest request, CancellationToken cancellationToken)
    {
        Document? document = await UserData.Documents.GetFirstOrDefaultAsync(new DocumentByIdTrackingSpec(request.DocumentId), cancellationToken);
        return document is not null && document.OrganizationId == request.OrganizationId ? document : null;
    }

    protected override bool IsQueued(Document document)
    {
        return document.ProcessingState is DocumentProcessingState.Queued;
    }

    protected override bool IsSameAttempt(Document loaded, Document current)
    {
        return loaded.ProcessingRequestedAt == current.ProcessingRequestedAt;
    }

    protected override string StateOf(Document document)
    {
        return document.ProcessingState.ToString();
    }

    protected override async Task ExecuteAsync(Document document, DocumentProcessingRequest request, CancellationToken cancellationToken)
    {
        Document withPages = await UserData.Documents.GetByIdAsync(document.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Document {document.Id} disappeared between the state check and its processing.");
        DocumentInfo documentInfo = new(withPages.Id) { Document = withPages };

        await _processing.ProcessDocumentsAsync([documentInfo], UserData, workflow: null, userId: request.UserId, cancellationToken: cancellationToken);

        if (withPages.ProcessingState is not DocumentProcessingState.Processed)
        {
            throw new InvalidOperationException(NOT_MARKED_PROCESSED_MESSAGE);
        }
    }

    protected override void RecordFailure(Document document, string message)
    {
        document.RecordProcessingFailure(message);
    }

    protected override string Describe(DocumentProcessingRequest request)
    {
        return $"Standalone processing of document {request.DocumentId}";
    }
}
