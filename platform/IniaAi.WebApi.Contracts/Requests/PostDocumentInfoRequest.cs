namespace IniaAi.WebApi.Contracts.Requests;

/// <summary>
/// Represents a request to post document information, classification, and associated tags.
/// </summary>
public class PostDocumentInfoRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the file to which the metadata applies.
    /// </summary>
    public required string FileId { get; set; }

    /// <summary>
    /// Gets or sets the classification of the document.
    /// </summary>
    /// <remarks>
    /// If not specified, this property defaults to an empty string.
    /// </remarks>
    public string DocumentClass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of tags associated with the document.
    /// </summary>
    /// <remarks>
    /// If not specified, this property defaults to an empty list.
    /// </remarks>
    public List<string> DocumentTags { get; set; } = [];

    /// <summary>
    /// Gets or sets whether OCR (Optical Character Recognition) should be used for processing this document.
    /// </summary>
    /// <remarks>
    /// Applicable for PDF, JPG, JPEG, and PNG documents.
    /// If not specified, this property defaults to <c>false</c>.
    /// </remarks>
    public bool ShouldUseOcr { get; set; }

    /// <summary>
    /// Gets or sets whether this document should be displayed in the final output.
    /// </summary>
    public bool ShouldBeDisplayedInOutput { get; set; } = true;
}
