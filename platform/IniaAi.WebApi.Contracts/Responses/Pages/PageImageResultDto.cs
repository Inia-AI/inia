namespace IniaAi.WebApi.Contracts.Responses.Pages;

/// <summary>
/// Represents the result of a page image request containing the binary data and caching metadata.
/// </summary>
public sealed class PageImageResultDto
{
    /// <summary>
    /// Gets the binary content of the image or text.
    /// </summary>
    public byte[] Content { get; init; } = [];

    /// <summary>
    /// Gets the MIME content type (e.g., "image/png", "image/jpeg", "text/plain").
    /// </summary>
    public string ContentType { get; init; } = "image/png";

    /// <summary>
    /// Gets the ETag value for HTTP caching.
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>
    /// Gets the last modified date for HTTP caching.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }
}
