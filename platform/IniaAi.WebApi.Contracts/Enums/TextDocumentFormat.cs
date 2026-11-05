namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// How pasted text is stored: as a plain text or a markdown document.
/// </summary>
public enum TextDocumentFormat
{
    /// <summary>Stored as <c>.txt</c> (text/plain).</summary>
    PlainText = 0,

    /// <summary>Stored as <c>.md</c> (text/markdown).</summary>
    Markdown = 1,
}
