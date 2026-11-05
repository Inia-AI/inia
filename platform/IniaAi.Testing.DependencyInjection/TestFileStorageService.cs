using IniaAi.Application.Common.Interfaces.Services;
using System.Collections.Concurrent;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// Test implementation of file storage service that uses in-memory storage. One instance is
/// shared by every consumer in a test's service provider, and a workflow run stores files from
/// parallel block legs, so the map must take concurrent writes — a plain dictionary here loses
/// entries under a concurrent resize, which surfaced as a session-bundle read-back miss.
/// </summary>
public class TestFileStorageService : IFileStorageService
{
    private readonly ConcurrentDictionary<string, byte[]> _files = [];

    public async Task<string> UploadAsync(string organizationId, string category, string key, Stream stream, string contentType)
    {
        string fullKey = $"{organizationId}/{category}/{key}";
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        _files[fullKey] = memoryStream.ToArray();
        return fullKey;
    }

    public async Task<Stream> DownloadAsync(string organizationId, string category, string key)
    {
        string fullKey = key.StartsWith($"{organizationId}/") ? key : $"{organizationId}/{category}/{key}";

        return _files.TryGetValue(fullKey, out byte[]? fileData)
            ? (Stream)await Task.FromResult(new MemoryStream(fileData))
            : throw new FileNotFoundException($"File not found: {fullKey}");
    }

    public async Task DeleteAsync(string organizationId, string category, string key)
    {
        string fullKey = key.StartsWith($"{organizationId}/") ? key : $"{organizationId}/{category}/{key}";
        _ = _files.TryRemove(fullKey, out _);
        await Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(string organizationId, string category, string key)
    {
        string fullKey = key.StartsWith($"{organizationId}/") ? key : $"{organizationId}/{category}/{key}";
        return await Task.FromResult(_files.ContainsKey(fullKey));
    }

    /// <summary>
    /// Helper method for tests to add files directly
    /// </summary>
    public void AddTestFile(string organizationId, string category, string key, byte[] content)
    {
        string fullKey = $"{organizationId}/{category}/{key}";
        _files[fullKey] = content;
    }

    /// <summary>
    /// Helper method for tests to get stored files
    /// </summary>
    public byte[]? GetTestFile(string organizationId, string category, string key)
    {
        string fullKey = $"{organizationId}/{category}/{key}";
        return _files.TryGetValue(fullKey, out byte[]? content) ? content : null;
    }

    /// <summary>
    /// Helper method for tests to clear all files
    /// </summary>
    public void ClearAllFiles()
    {
        _files.Clear();
    }
}
