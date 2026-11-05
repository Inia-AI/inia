namespace IniaAi.WebApi.Contracts.Responses.Documents;

/// <summary>
/// Full document projection for detail views. Extends <see cref="DocumentSummaryDto"/>
/// with descriptive metadata and a page count. Page content is fetched via the
/// separate /documents/{id}/pages endpoint to keep this payload bounded.
/// </summary>
public sealed class DocumentDetailDto : DocumentSummaryDto
{
    /// <summary>Gets or sets a short description of the document, if authored.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the sources the document was ingested from (e.g. "User Upload").</summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>Gets or sets the topic metadata extracted during processing.</summary>
    public string? Topics { get; set; }

    /// <summary>Gets or sets the table-of-contents metadata extracted during processing.</summary>
    public string? TableOfContents { get; set; }

    /// <summary>Gets or sets the number of pages extracted for this document.</summary>
    public int PageCount { get; set; }

    /// <summary>Gets or sets the ID of the backing FileMetadata row.</summary>
    public string FileMetadataId { get; set; } = string.Empty;
}
