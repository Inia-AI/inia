using Amazon.S3;
using Amazon.S3.Model;
using IniaAi.Application.Common.Interfaces.Services;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;

namespace IniaAi.Infrastructure.Storage;

/// <summary>An object a store's bucket holds: the file key it was stored under, its object key, and when it was written.</summary>
public sealed record StoredObjectEntry(string Key, string ObjectKey, DateTime? LastModified);

/// <summary>
/// Stores files in Amazon S3, or in any store that speaks the S3 API, such as MinIO, through the AWS
/// SDK. The layout decides where a file lives. A read streams the object as the store answers it,
/// with its size known from the answer, and an upload sends the caller's stream by its length.
/// </summary>
public sealed class ObjectStorageService : IFileStorageService
{
    private const string NO_SUCH_KEY = "NoSuchKey";
    private const string NO_SUCH_BUCKET = "NoSuchBucket";
    private const string BUCKET_ALREADY_OWNED = "BucketAlreadyOwnedByYou";
    private const string BUCKET_ALREADY_EXISTS = "BucketAlreadyExists";

    private readonly IAmazonS3 _client;
    private readonly IObjectLayout _layout;
    private readonly ConcurrentDictionary<string, byte> _bucketsMade = new();

    /// <summary>
    /// Initializes a new instance of <see cref="ObjectStorageService"/>.
    /// </summary>
    /// <param name="client">The S3 client for the store.</param>
    /// <param name="layout">Where the store keeps each file.</param>
    public ObjectStorageService(IAmazonS3 client, IObjectLayout layout)
    {
        _client = client;
        _layout = layout;
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(string organizationId, string category, string key, Stream content, string contentType)
    {
        ObjectLocation location = _layout.Locate(organizationId, category, key);
        if (_layout.Bucket is null)
        {
            await EnsureBucketAsync(location.Bucket);
        }

        var request = new PutObjectRequest
        {
            BucketName = location.Bucket,
            Key = location.Key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = contentType,
        };
        if (!content.CanSeek)
        {
            request.Headers.ContentLength = StoredObjectStream.LengthOf(content)
                ?? throw new ArgumentException("The store sends a file by its length, and this stream cannot say it.", nameof(content));
        }

        _ = await _client.PutObjectAsync(request);
        return key;
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string organizationId, string category, string path)
    {
        AmazonS3Exception? missing = null;
        foreach (ObjectLocation location in _layout.Candidates(organizationId, category, path))
        {
            try
            {
                GetObjectResponse response = await _client.GetObjectAsync(location.Bucket, location.Key);
                return new StoredObjectStream(response.ResponseStream, response.ContentLength, response);
            }
            catch (AmazonS3Exception ex) when (IsMissing(ex))
            {
                missing = ex;
            }
        }

        throw new FileNotFoundException($"Object '{path}' of organization '{organizationId}' not found", missing);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string organizationId, string category, string path)
    {
        foreach (ObjectLocation location in _layout.Candidates(organizationId, category, path))
        {
            try
            {
                _ = await _client.DeleteObjectAsync(location.Bucket, location.Key);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == NO_SUCH_BUCKET)
            {
                // A bucket per organization that was never created holds nothing to delete.
            }
        }
    }

    /// <summary>Every object of the layout's single bucket, page by page.</summary>
    public async IAsyncEnumerable<StoredObjectEntry> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string bucket = SingleBucket();
        string? continuation = null;
        do
        {
            ListObjectsV2Response page = await _client.ListObjectsV2Async(
                new ListObjectsV2Request { BucketName = bucket, ContinuationToken = continuation },
                cancellationToken);
            foreach (S3Object stored in page.S3Objects ?? [])
            {
                yield return new StoredObjectEntry(_layout.KeyOf(stored.Key), stored.Key, stored.LastModified);
            }

            continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (continuation is not null);
    }

    /// <summary>Deletes one object of the layout's single bucket by its object key, as <see cref="ListAsync"/> named it.</summary>
    public async Task DeleteObjectAsync(string objectKey, CancellationToken cancellationToken)
    {
        _ = await _client.DeleteObjectAsync(SingleBucket(), objectKey, cancellationToken);
    }

    /// <summary>
    /// One cheap call that throws when the store does not answer: a list of at most one object of the
    /// single bucket, or of the buckets for a layout with a bucket per organization.
    /// </summary>
    public async Task CheckReachableAsync(CancellationToken cancellationToken)
    {
        if (_layout.Bucket is string bucket)
        {
            _ = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 }, cancellationToken);
        }
        else
        {
            _ = await _client.ListBucketsAsync(cancellationToken);
        }
    }

    private static bool IsMissing(AmazonS3Exception ex)
    {
        return ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode is NO_SUCH_KEY or NO_SUCH_BUCKET;
    }

    private string SingleBucket()
    {
        return _layout.Bucket ?? throw new InvalidOperationException("This store keeps a bucket per organization, so it has no single bucket to walk.");
    }

    private async Task EnsureBucketAsync(string bucket)
    {
        if (_bucketsMade.ContainsKey(bucket))
        {
            return;
        }

        try
        {
            _ = await _client.PutBucketAsync(bucket);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is BUCKET_ALREADY_OWNED or BUCKET_ALREADY_EXISTS)
        {
            // The bucket was made by an earlier upload or another host.
        }

        _ = _bucketsMade.TryAdd(bucket, 0);
    }
}
