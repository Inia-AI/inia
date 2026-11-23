namespace IniaAi.WebApi.Contracts.Responses.Files;

/// <summary>
/// Represents the result of a file content request: the content as it streams from its store, and its metadata.
/// </summary>
public sealed class FileResult
{
    /// <summary>
    /// Gets the content of the file, streamed from its store. Whoever sends it disposes it.
    /// </summary>
    public Stream Content { get; init; } = Stream.Null;

    /// <summary>
    /// Gets the size of the file in bytes, or null when the store does not say it before the read.
    /// </summary>
    public long? Length { get; init; }

    /// <summary>
    /// Gets the MIME content type of the file.
    /// </summary>
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>
    /// Gets the filename of the file.
    /// </summary>
    public string FileName { get; init; } = "document";

    /// <summary>
    /// Gets the content disposition type ("inline" for preview, "attachment" for download).
    /// </summary>
    public string ContentDisposition { get; init; } = "attachment";
}
