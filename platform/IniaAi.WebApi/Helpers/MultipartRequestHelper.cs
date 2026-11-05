// for file uploads, see:
// https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-8.0#upload-large-files-with-streaming
using Microsoft.Net.Http.Headers;

namespace IniaAi.WebApi.Helpers;

/// <summary>
/// Provides helper methods for processing multipart/form-data requests.
/// </summary>
public static class MultipartRequestHelper
{
    /// <summary>
    /// Gets the boundary from the Content-Type header. The boundary
    /// parameter is required in the multipart/form-data content type.
    /// </summary>
    /// <param name="contentType">The Content-Type header value.</param>
    /// <param name="lengthLimit">The maximum allowable length of the boundary.</param>
    /// <returns>The boundary string.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown if the boundary is missing or exceeds the length limit.
    /// </exception>
    public static string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit)
    {
        string? boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;

        return string.IsNullOrWhiteSpace(boundary)
            ? throw new InvalidDataException("Missing content-type boundary.")
            : boundary.Length > lengthLimit
                ? throw new InvalidDataException($"Multipart boundary length limit {lengthLimit} exceeded.")
                : boundary;
    }

    /// <summary>
    /// Determines whether the specified content type is a multipart content type.
    /// </summary>
    /// <param name="contentType">The content type to check.</param>
    /// <returns>
    /// <c>true</c> if the content type is multipart; otherwise, <c>false</c>.
    /// </returns>
    public static bool IsMultipartContentType(string contentType)
    {
        return !string.IsNullOrEmpty(contentType)
               && contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the Content-Disposition header represents form data.
    /// </summary>
    /// <param name="contentDisposition">The Content-Disposition header value.</param>
    /// <returns>
    /// <c>true</c> if the Content-Disposition is form data; otherwise, <c>false</c>.
    /// </returns>
    public static bool HasFormDataContentDisposition(ContentDispositionHeaderValue contentDisposition)
    {
        // Content-Disposition: form-data; name="key";
        return contentDisposition is not null
            && contentDisposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(contentDisposition.FileName.Value)
            && string.IsNullOrEmpty(contentDisposition.FileNameStar.Value);
    }

    /// <summary>
    /// Determines whether the Content-Disposition header represents a file.
    /// </summary>
    /// <param name="contentDisposition">The Content-Disposition header value.</param>
    /// <returns>
    /// <c>true</c> if the Content-Disposition is a file; otherwise, <c>false</c>.
    /// </returns>
    public static bool HasFileContentDisposition(ContentDispositionHeaderValue contentDisposition)
    {
        // Content-Disposition: form-data; name="myfile1"; filename="Misc 002.jpg"
        return contentDisposition is not null
            && contentDisposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrEmpty(contentDisposition.FileName.Value)
                || !string.IsNullOrEmpty(contentDisposition.FileNameStar.Value));
    }
}
