using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Dto;

/// <summary>
/// Represents metadata information for a document.
/// </summary>
public sealed class FileDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the file.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the file.
    /// </summary>
    public required string Filename { get; set; }

    /// <summary>
    /// Gets or sets the media type of the file.
    /// </summary>
    public required MediaType MediaType { get; set; }

    /// <summary>
    /// Gets or sets any warnings detected during upload (e.g., structural issues with the file).
    /// Null or empty if no warnings.
    /// </summary>
    public List<string>? Warnings { get; set; }

    /// <summary>
    /// True when this upload created the document. False when the platform reused a document
    /// the organization already held under the same name and size.
    /// </summary>
    public bool Created { get; set; }

    /// <summary>
    /// True when processing reads this file into pages. False for an email or a legacy Word file,
    /// which is stored as a file only and refused by the process endpoint.
    /// </summary>
    public bool ProducesPages { get; set; }

    /// <summary>
    /// The pages the platform counted for this file at upload, with no model call: a PDF's pages, and the text of other
    /// formats at 4,000 characters a page. For an email, the pages of its body. Null when the file could not be read or
    /// makes no pages.
    /// </summary>
    public int? CountedPages { get; set; }

    /// <summary>
    /// For an email, the counted pages of the files a run can extract from it. A run that extracts them reads these
    /// pages as well as the body's. Null for any other file, and when the email could not be read.
    /// </summary>
    public int? CountedAttachmentPages { get; set; }
}
