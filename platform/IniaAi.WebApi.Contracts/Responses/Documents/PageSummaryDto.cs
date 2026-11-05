namespace IniaAi.WebApi.Contracts.Responses.Documents;

/// <summary>
/// Lightweight projection of a single page for listing views. Designed to be filtered
/// and paginated via OData; page body text is truncated to a preview to keep
/// payloads small — full text is retrieved via a dedicated page endpoint.
/// </summary>
public class PageSummaryDto
{
    /// <summary>Gets or sets the page ID.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the parent document ID.</summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Gets or sets the logical page number within the document.</summary>
    public int PageNumber { get; set; }

    /// <summary>Gets or sets the original page number within the source file.</summary>
    public int FilePageNumber { get; set; }

    /// <summary>Gets or sets a preview of the page text (first N characters).</summary>
    public string? TextPreview { get; set; }

    /// <summary>Gets or sets the AI-generated overview, if available.</summary>
    public string? Overview { get; set; }

    /// <summary>Gets or sets the location tag (full-page, header, etc.) as a string.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Gets or sets the processing status (NotStarted, Processing, Completed, Failed) as a string.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the OCR fidelity check judged this page's
    /// transcription unreliable; such pages carry no extracted text and are routed by image.
    /// </summary>
    public bool OcrNeedsReview { get; set; }

    /// <summary>Gets or sets a value indicating whether an embedding vector has been generated for this page.</summary>
    public bool HasEmbedding { get; set; }

    /// <summary>Gets or sets the embedding model name, if an embedding exists.</summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>Gets or sets the source URL for pages derived from web content, if any.</summary>
    public string? SourceUrl { get; set; }
}
