namespace IniaAi.Domain.Enums;

/// <summary>
/// Where a document stands on the standalone processing path (upload or paste →
/// pages), derived from the timestamps the request and the worker leave on the
/// document. Documents processed inside a workflow execution read as
/// <see cref="Processed"/> without ever having been <see cref="Queued"/>.
/// </summary>
public enum DocumentProcessingState
{
    NotRequested,
    Queued,
    Processed,
    Failed,
}
