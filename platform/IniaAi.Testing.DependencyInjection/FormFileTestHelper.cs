using Microsoft.AspNetCore.Http;

namespace IniaAi.Testing.DependencyInjection;

public static class FormFileTestHelper
{
    /// <summary>
    /// Loads a real file from disk and builds a valid IFormFile for testing.
    /// </summary>
    /// <param name="filePath">Path to the file (relative or absolute).</param>
    /// <param name="contentType">Optional MIME type.</param>
    /// <returns>An IFormFile instance wrapping the file content.</returns>
    public static IFormFile CreateFromFile(string filePath, string contentType = "application/octet-stream")
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        return CreateFromBytes(bytes, Path.GetFileName(filePath), contentType);
    }

    /// <summary>
    /// Builds a valid IFormFile from in-memory bytes for testing.
    /// </summary>
    /// <param name="bytes">The file content.</param>
    /// <param name="fileName">The file name, including extension.</param>
    /// <param name="contentType">Optional MIME type.</param>
    /// <returns>An IFormFile instance wrapping the given content.</returns>
    public static IFormFile CreateFromBytes(byte[] bytes, string fileName, string contentType = "application/octet-stream")
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
