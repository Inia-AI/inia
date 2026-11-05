namespace IniaAi.WebApi.Presentation.Contracts;

/// <summary>
/// Represents the request model for uploading a file via multipart/form-data.
/// Needed for Swagger
/// </summary>
/// <remarks>
/// This model is used to encapsulate the file upload request data.
/// </remarks>
public class UploadFormRequest
{
    /// <summary>
    /// Gets or sets the file to be uploaded.
    /// </summary>
    /// <remarks>
    /// Supported formats include .pdf, .jpg, .png, .jpeg, .csv, .xlsx, .xlsm and .docx, and in the paid build .xls, .msg and .eml.
    /// </remarks>
    public required IFormFile File { get; set; }

    /// <summary>
    /// Gets or sets the optional document category ID.
    /// </summary>
    /// <remarks>
    /// Stamped on the document. During a workflow run, the category of the document's class takes precedence.
    /// </remarks>
    public string? DocumentCategoryId { get; set; }

    /// <summary>
    /// Gets or sets the optional password for an encrypted PDF, Excel, or Word file.
    /// </summary>
    /// <remarks>
    /// Used once to decrypt the file at upload; the decrypted copy is stored and the password is never persisted.
    /// </remarks>
    public string? Password { get; set; }
}
