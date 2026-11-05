namespace IniaAi.Domain.Enums;

/// <summary>
/// Specifies the supported export formats for workflow execution documents.
/// </summary>
public enum ExportFormat
{
    /// <summary>
    /// Microsoft Word document format (.docx).
    /// </summary>
    Word,

    /// <summary>
    /// Portable Document Format (.pdf).
    /// </summary>
    Pdf,

    /// <summary>
    /// Markdown document format (.md).
    /// </summary>
    Markdown
}