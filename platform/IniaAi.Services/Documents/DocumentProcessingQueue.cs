using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Services.Queues;

namespace IniaAi.Services.Documents;

/// <summary>
/// The standalone document processing hand-off. A request lost to a restart leaves the
/// document <c>Queued</c> with nothing behind it; the remedy is asking again.
/// </summary>
public sealed class DocumentProcessingQueue : WorkQueue<DocumentProcessingRequest>;
