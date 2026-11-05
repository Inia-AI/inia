using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Storage;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace IniaAi.Infrastructure.Storage;

/// <summary>
/// MinIO storage service implementation for storing files with org-based buckets.
/// </summary>
public class MinioStorageService : IFileStorageService
{
    private readonly IMinioClient _minioClient;
    private readonly IDictionary<string, string> _categoryMap;
    private readonly string? _fixedBucketName;

    /// <summary>
    /// Initializes a new instance of <see cref="MinioStorageService"/>.
    /// </summary>
    /// <param name="minioClient">MinIO client instance.</param>
    /// <param name="fileStorageOptions">File storage configuration options.</param>
    public MinioStorageService(IMinioClient minioClient, IOptions<FileStorageSettings> fileStorageOptions)
    {
        _minioClient = minioClient;
        _categoryMap = fileStorageOptions.Value.StorageBuckets ?? [];
        _fixedBucketName = fileStorageOptions.Value.MinIO?.BucketName;
    }

    private static string Normalize(string input)
    {
        return input.ToLower().Replace("_", "-").Replace(".", "-");
    }

    private string ResolveBucketName(string orgId, string category)
    {
        // On-premise / single-tenant mode: all files go to one pre-created bucket.
        // Per-org bucket isolation is not needed because the customer is the sole tenant
        // and is responsible for their own data protection.
        if (!string.IsNullOrEmpty(_fixedBucketName))
        {
            return _fixedBucketName;
        }

        if (!_categoryMap.TryGetValue(category, out string? mapped))
        {
            mapped = category; // Default to category name if not mapped
        }

        return $"{Normalize(orgId)}-{Normalize(mapped)}";
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(string organizationId, string category, string key, Stream content, string contentType)
    {
        string bucketName = ResolveBucketName(organizationId, category);

        // Ensure bucket exists
        BucketExistsArgs bucketExistsArgs = new BucketExistsArgs().WithBucket(bucketName);
        bool bucketExists = await _minioClient.BucketExistsAsync(bucketExistsArgs);

        if (!bucketExists)
        {
            MakeBucketArgs makeBucketArgs = new MakeBucketArgs().WithBucket(bucketName);
            await _minioClient.MakeBucketAsync(makeBucketArgs);
        }

        // Ensure we have a seekable stream and know the length
        Stream uploadStream;
        long uploadLength;
        if (content.CanSeek)
        {
            uploadStream = content;
            uploadLength = content.Length;
            if (uploadStream.Position != 0)
            {
                uploadStream.Position = 0;
            }
        }
        else
        {
            var memoryStream = new MemoryStream();
            content.CopyTo(memoryStream);
            memoryStream.Position = 0;
            uploadStream = memoryStream;
            uploadLength = memoryStream.Length;
        }

        try
        {
            PutObjectArgs putObjectArgs = new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(key)
                .WithStreamData(uploadStream)
                .WithObjectSize(uploadLength)
                .WithContentType(contentType);

            _ = await _minioClient.PutObjectAsync(putObjectArgs);

            // Verify upload by checking if object exists
            StatObjectArgs statObjectArgs = new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(key);

            ObjectStat objectStat = await _minioClient.StatObjectAsync(statObjectArgs);

            return string.IsNullOrEmpty(objectStat.ETag)
                ? throw new InvalidOperationException($"Upload verification failed for object '{key}' in bucket '{bucketName}' - no ETag found")
                : key;
        }
        catch (MinioException ex)
        {
            throw new InvalidOperationException($"MinIO upload failed for object '{key}' in bucket '{bucketName}': {ex.Message}", ex);
        }
        finally
        {
            if (uploadStream != content)
            {
                uploadStream?.Dispose(); // Only dispose if we created a new stream
            }
        }
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string organizationId, string category, string path)
    {
        // Input validation
        if (string.IsNullOrEmpty(organizationId))
        {
            throw new ArgumentNullException(nameof(organizationId));
        }

        if (string.IsNullOrEmpty(category))
        {
            throw new ArgumentNullException(nameof(category));
        }

        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }

        string bucketName = ResolveBucketName(organizationId, category);
        var memoryStream = new MemoryStream();

        try
        {
            // First check if object exists using StatObjectAsync
            StatObjectArgs statObjectArgs = new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(path);

            ObjectStat objectStat = await _minioClient.StatObjectAsync(statObjectArgs);

            GetObjectArgs getObjectArgs = new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(path)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream));

            _ = await _minioClient.GetObjectAsync(getObjectArgs);

            // Validate that we actually got data
            if (memoryStream.Length == 0)
            {
                throw new InvalidOperationException($"Downloaded object '{path}' from bucket '{bucketName}' but received no data");
            }

            // Validate against expected size from StatObject
            if (objectStat.Size > 0 && memoryStream.Length != objectStat.Size)
            {
                throw new InvalidOperationException(
                    $"Downloaded object '{path}' size mismatch: expected {objectStat.Size} bytes, got {memoryStream.Length} bytes");
            }

            // Check for XML error responses (common MinIO issue)
            memoryStream.Position = 0;
            byte[] buffer = new byte[Math.Min(200, memoryStream.Length)];
            _ = memoryStream.Read(buffer, 0, buffer.Length);
            string headerContent = System.Text.Encoding.UTF8.GetString(buffer);

            if (headerContent.Contains("<?xml") && headerContent.Contains("<Code>"))
            {
                throw new InvalidOperationException($"MinIO returned error response instead of file content for '{path}' in bucket '{bucketName}'");
            }

            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (MinioException ex) when (ex.Message.Contains("NoSuchKey") || ex.Message.Contains("NoSuchBucket") || ex.Message.Contains("The specified key does not exist"))
        {
            memoryStream?.Dispose();
            throw new FileNotFoundException($"Object '{path}' not found in bucket '{bucketName}'", ex);
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            memoryStream?.Dispose();
            throw new InvalidOperationException(
                $"MinIO download failed - Bucket: '{bucketName}', Path: '{path}'. Error: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string organizationId, string category, string path)
    {
        string bucketName = ResolveBucketName(organizationId, category);

        RemoveObjectArgs removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(bucketName)
            .WithObject(path);

        await _minioClient.RemoveObjectAsync(removeObjectArgs);
    }
}
