using IniaAi.Domain.Enums;
using System.Linq.Expressions;

namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// The one derivation of a document's standalone processing state from the three stamps it
/// carries — an expression so list projections translate it and the entity evaluates it.
/// </summary>
public static class DocumentProcessingStates
{
    public static readonly Expression<Func<Document, DocumentProcessingState>> Of = document =>
        document.ProcessingError != null
            ? DocumentProcessingState.Failed
            : document.ProcessingRequestedAt != null && (document.ProcessedAt == null || document.ProcessedAt < document.ProcessingRequestedAt)
                ? DocumentProcessingState.Queued
                : document.ProcessedAt == null
                    ? DocumentProcessingState.NotRequested
                    : DocumentProcessingState.Processed;

    private static readonly Func<Document, DocumentProcessingState> _of = Of.Compile();

    public static DocumentProcessingState Evaluate(Document document)
    {
        return _of(document);
    }
}
