namespace IniaAi.WebApi.Contracts.Responses.Documents;

/// <summary>
/// One page with its full text — what an extraction reads for this page.
/// </summary>
public sealed class PageDetailDto : PageSummaryDto
{
    /// <summary>The page's full text; null when nothing was extracted.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// The text an extraction reads for this page: the text, or the overview when the page is
    /// image-only and routed by image.
    /// </summary>
    public string? ReadableText { get; set; }

    /// <summary>The number of dimensions of the page's embedding vector; null when the page has none.</summary>
    public int? EmbeddingDimensions { get; set; }

    /// <summary>
    /// The section titles the document's table of contents gives this page, as one comma-separated
    /// string; null when the document has no table of contents.
    /// </summary>
    public string? SectionTitles { get; set; }
}
