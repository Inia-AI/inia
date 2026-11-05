using Amazon.S3;
using Amazon.S3.Model;
using IniaAi.Application.Common.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace IniaAi.Infrastructure.Storage;

/// <summary>
/// AWS S3 storage service implementation for storing files with org-based buckets.
/// </summary>
public class S3StorageService : IFileStorageService
{
    private const string NO_SUCH_KEY = "NoSuchKey";

    private readonly IAmazonS3 _s3Client;
    private readonly IDictionary<string, string> _categoryMap;

    /// <summary>
    /// Initializes a new instance of <see cref="S3StorageService"/>.
    /// </summary>
    /// <param name="s3Client">The AWS S3 client injected by DI.</param>
    /// <param name="configuration">App configuration for category mapping.</param>
    public S3StorageService(IAmazonS3 s3Client, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _categoryMap = configuration.GetSection("StorageBuckets")
            .Get<Dictionary<string, string>>()!;
    }

    private static string Normalize(string input)
    {
        return input.ToLower().Replace("_", "-").Replace(".", "-");
    }

    private string ResolveBucketName(string orgId, string category)
    {
        return !_categoryMap.TryGetValue(category, out string? mapped)
            ? throw new InvalidOperationException($"Unknown storage category: {category}")
            : $"org-{Normalize(orgId)}-{Normalize(mapped)}";
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(string orgId, string category, string key, Stream content, string contentType)
    {
        string bucket = ResolveBucketName(orgId, category);
        await _s3Client.EnsureBucketExistsAsync(bucket);

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType
        };

        _ = await _s3Client.PutObjectAsync(request);
        return key;
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string orgId, string category, string path)
    {
        string bucket = ResolveBucketName(orgId, category);

        GetObjectResponse response;

        try
        {
            response = await _s3Client.GetObjectAsync(bucket, path);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == NO_SUCH_KEY)
        {
            throw new FileNotFoundException($"Object '{path}' not found in bucket '{bucket}'", ex);
        }

        var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string orgId, string category, string path)
    {
        string bucket = ResolveBucketName(orgId, category);
        _ = await _s3Client.DeleteObjectAsync(bucket, path);
    }
}
