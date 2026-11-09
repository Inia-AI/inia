using IniaAi.Common.Enums;

namespace IniaAi.Common.Interfaces;

/// <summary>
/// Counts the pages of an uploaded file from its bytes, in one pass and with no model call, for
/// <see cref="Domain.Entities.Documents.Document.CountedPages"/>. The rule for each format is on the open
/// implementation, <c>DocumentPageCounter</c>.
/// </summary>
public interface IDocumentPageCounter
{
    /// <returns>Null when the file cannot be read, or its type makes no pages.</returns>
    int? Count(byte[] bytes, MediaType mediaType, string fileName);
}
