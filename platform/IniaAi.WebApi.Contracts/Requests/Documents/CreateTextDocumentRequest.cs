using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.Documents;

/// <summary>
/// Pasted text or markdown that becomes a document of its own: stored as a file, then
/// processed into pages like an upload — with no format extraction in between.
/// </summary>
public record CreateTextDocumentRequest
{
    /// <summary>The document name; the format's extension is appended when missing.</summary>
    public required string Name { get; init; }

    /// <summary>The text itself.</summary>
    public required string Text { get; init; }

    /// <summary>Plain text by default; markdown keeps its structure for readers that render it.</summary>
    public TextDocumentFormat Format { get; init; } = TextDocumentFormat.PlainText;
}
