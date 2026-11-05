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
}
