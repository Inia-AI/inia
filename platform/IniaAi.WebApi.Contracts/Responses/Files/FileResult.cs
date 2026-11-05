namespace IniaAi.WebApi.Contracts.Responses.Files;

/// <summary>
/// Represents the result of a file content request containing the binary data and metadata.
/// </summary>
public sealed class FileResult
{
    /// <summary>
    /// Gets the binary content of the file.
    /// </summary>
    public byte[] Content { get; init; } = [];

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
