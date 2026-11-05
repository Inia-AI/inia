using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;

namespace IniaAi.Infrastructure.Utilities;

/// <summary>
/// Utility class for determining MIME content types from file extensions
/// </summary>
public static class ContentTypeHelper
{
    /// <summary>
    /// Gets the MIME content type based on file extension
    /// </summary>
    /// <param name="extension">The file extension (with or without leading dot)</param>
    /// <returns>The corresponding MIME content type</returns>
    public static string GetContentTypeFromExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        string normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
        MediaType mediaType = Utils.DetermineMediaTypeFromExtension(normalizedExtension);

        return mediaType is MediaType.Unknown
            ? "application/octet-stream"
            : mediaType.GetEnumMember();
    }

    /// <summary>
    /// Gets the content type from a filename
    /// </summary>
    /// <param name="fileName">The filename</param>
    /// <returns>The corresponding MIME content type</returns>
    public static string GetContentTypeFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "application/octet-stream";
        }

        string extension = Path.GetExtension(fileName);
        return GetContentTypeFromExtension(extension);
    }
}
