namespace IniaAi.WebApp.Models;

/// <summary>
/// The run-level choice for how email documents are processed: bodies are always rendered to PDF
/// inside the job; extraction of attachments (with the chosen document class) is the opt-in.
/// Owned by the upload card and read at job start.
/// </summary>
public class EmailIntakeSelection
{
    public bool ExtractAttachments { get; set; }

    public string? AttachmentsDocumentClass { get; set; }
}
