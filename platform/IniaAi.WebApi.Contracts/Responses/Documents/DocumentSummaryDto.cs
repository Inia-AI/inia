using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.Documents;

/// <summary>
/// Lightweight document projection for list views. Contains only metadata fields
/// suitable for rendering a table row; does not include pages or file bytes.
/// Designed to be served via OData $filter/$orderby/$top/$skip/$count.
/// </summary>
public class DocumentSummaryDto
{
    /// <summary>Gets or sets the document ID.</summary>
    public required string Id { get; set; }

    /// <summary>Gets or sets the display name of the document.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the media type (PDF, Image, etc.) as a string.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the size of the underlying file, in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets the file extension including the leading dot (e.g. ".pdf").</summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>Gets or sets the user who uploaded the document, if known.</summary>
    public string? UserId { get; set; }

    /// <summary>Gets or sets the document category ID, if assigned.</summary>
    public string? DocumentCategoryId { get; set; }

    /// <summary>Gets or sets the workflow type ID, if the document is pinned to a static workflow.</summary>
    public string? WorkflowTypeId { get; set; }

    /// <summary>Gets or sets the free-form classification tag, if set.</summary>
    public string? Class { get; set; }

    /// <summary>Gets or sets whether the file arrived password-protected and was stored as a decrypted copy.</summary>
    public bool WasPasswordProtected { get; set; }

    /// <summary>Gets or sets where the document stands on the standalone processing path.</summary>
    public DocumentProcessingState ProcessingState { get; set; }

    /// <summary>Gets or sets when standalone processing was last requested (UTC); null when never requested.</summary>
    public DateTime? ProcessingRequestedAt { get; set; }

    /// <summary>Gets or sets when the pipeline last finished producing pages (UTC), on any path.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Gets or sets why the last standalone processing attempt failed; null otherwise.</summary>
    public string? ProcessingError { get; set; }

    /// <summary>Gets or sets the UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC last-updated timestamp, if available.</summary>
    public DateTime? UpdatedAt { get; set; }
}
