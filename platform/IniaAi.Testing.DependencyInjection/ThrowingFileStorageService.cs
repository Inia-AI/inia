using IniaAi.Application.Common.Interfaces.Services;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// Storage that cannot be reached, for the paths that exist only because it can happen. Not a
/// stand-in for <see cref="TestFileStorageService"/> — a test that asserts what was stored uses
/// that one; this is for asserting what a caller does when storage is gone.
/// </summary>
public class ThrowingFileStorageService : IFileStorageService
{
    private const string MESSAGE = "storage is unreachable";

    public Task<string> UploadAsync(
        string organizationId, string category, string key, Stream content, string contentType)
    {
        throw new IOException(MESSAGE);
    }

    public Task<Stream> DownloadAsync(string organizationId, string category, string key)
    {
        throw new IOException(MESSAGE);
    }

    public Task DeleteAsync(string organizationId, string category, string key)
    {
        throw new IOException(MESSAGE);
    }

    public Task<bool> ExistsAsync(string organizationId, string category, string key)
    {
        throw new IOException(MESSAGE);
    }
}
