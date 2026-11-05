using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Contracts.Dto.Citations;

/// <summary>
/// Base class for all citation source DTOs.
/// Provides polymorphic support for different types of sources (document, URL, block).
/// </summary>
[JsonDerivedType(typeof(DocumentCitationSourceDto), typeDiscriminator: "Document")]
[JsonDerivedType(typeof(UrlCitationSourceDto), typeDiscriminator: "Url")]
[JsonDerivedType(typeof(BlockCitationSourceDto), typeDiscriminator: "Block")]
public abstract class CitationSourceDto
{
    /// <summary>
    /// Gets the type discriminator for this source.
    /// </summary>
    public abstract string Type { get; }

    /// <summary>
    /// Gets or sets the text that was cited from this source.
    /// </summary>
    public string CitedText { get; set; } = string.Empty;
}

/// <summary>
/// Represents a citation source from an uploaded document.
/// </summary>
public class DocumentCitationSourceDto : CitationSourceDto
{
    /// <inheritdoc />
    public override string Type => "Document";

    /// <summary>
    /// Gets or sets the unique identifier of the document page.
    /// </summary>
    public string PageId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the page number within the document.
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Gets or sets the name of the document.
    /// </summary>
    public string? DocumentName { get; set; }

    /// <summary>
    /// Gets or sets the 1-based physical page of the file the citation resolved to
    /// (0 when the citation is unverified and claimed no page).
    /// </summary>
    public int FilePageNumber { get; set; }

    /// <summary>
    /// Gets or sets whether the cited text could not be located in the document's pages —
    /// an unverified citation is shown as unchecked, never as confirmed.
    /// </summary>
    public bool Unverified { get; set; }
}

/// <summary>
/// Represents a citation source from a web URL.
/// </summary>
public class UrlCitationSourceDto : CitationSourceDto
{
    /// <inheritdoc />
    public override string Type => "Url";

    /// <summary>
    /// Gets or sets the URL of the web source.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Represents a citation source from a previous workflow block.
/// </summary>
public class BlockCitationSourceDto : CitationSourceDto
{
    /// <inheritdoc />
    public override string Type => "Block";

    /// <summary>
    /// Gets or sets the unique identifier of the source block.
    /// </summary>
    public string BlockId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the source block.
    /// </summary>
    public string? BlockTitle { get; set; }

    /// <summary>
    /// Gets or sets the upstream sources that this block references.
    /// Provides the full provenance chain back to original documents.
    /// </summary>
    public List<CitationSourceDto> UpstreamSources { get; set; } = [];

    /// <summary>
    /// Gets or sets the pages and links from the block's cited-source list whose excerpt states
    /// the fact this citation marks, each excerpted around that fact. Empty when none does.
    /// </summary>
    public List<CitationSourceDto> MatchingSources { get; set; } = [];
}
