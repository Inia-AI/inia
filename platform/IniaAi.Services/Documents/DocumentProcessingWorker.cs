using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Services.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Services.Documents;

/// <summary>
/// Drains the standalone document processing queue; a failing document carries the error and
/// can be asked again.
/// </summary>
public class DocumentProcessingWorker : QueueWorker<DocumentProcessingRequest>
{
    private const int MAX_CONCURRENT_DOCUMENTS = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentProcessingWorker"/> class.
    /// </summary>
    public DocumentProcessingWorker(
        ILogger<DocumentProcessingWorker> logger,
        IWorkQueueReader<DocumentProcessingRequest> queue,
        IServiceScopeFactory scopeFactory)
        : base(logger, queue, scopeFactory)
    {
    }

    /// <inheritdoc />
    protected override int MaxConcurrency => MAX_CONCURRENT_DOCUMENTS;

    /// <inheritdoc />
    protected override string Describe(DocumentProcessingRequest request)
    {
        return $"Standalone processing request for document {request.DocumentId}";
    }
}
