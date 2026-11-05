namespace IniaAi.WebApi.Contracts.Requests.WorkflowExecutions;

/// <summary>
/// Metadata for a single file in the submit workflow execution request.
/// Matched to binary file parts by array index.
/// </summary>
public sealed class SubmitFileMetadata
{
    /// <summary>
    /// Gets or sets the document class to assign to this file.
    /// If null, the file is uploaded without a document class assignment.
    /// </summary>
    public string? DocumentClass { get; set; }

    /// <summary>
    /// Gets or sets the optional password for an encrypted PDF, Excel, or Word file.
    /// Used once to decrypt the file at upload; the decrypted copy is stored and the password is never persisted.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets whether this file (an .eml or .msg email) should be expanded server-side
    /// into its rendered body PDF, its supported attachments, and the raw email as an audit
    /// artifact. Only valid for .eml and .msg files.
    /// </summary>
    public bool ExpandEmail { get; set; }

    /// <summary>
    /// Gets or sets the document class assigned to every file extracted from an expanded email
    /// (attachments, archive entries, downloaded files, and the raw email itself).
    /// The body PDF inherits <see cref="DocumentClass"/> instead. Null means no class.
    /// </summary>
    public string? AttachmentsDocumentClass { get; set; }
}
