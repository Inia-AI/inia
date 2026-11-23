using Amazon.S3;
using Amazon.S3.Model;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Storage;

namespace IniaAi.WebApi.IntegrationTests.Storage;

/// <summary>
/// A throwaway bucket in a real S3-compatible store, reached at the address in <c>INIA_TEST_S3</c>
/// (for example a local SeaweedFS at <c>http://localhost:8333</c>). The test makes the bucket, as a
/// stack does, and removes it with everything in it. A test without the variable skips.
/// </summary>
internal sealed class ObjectStoreHarness : IAsyncDisposable
{
    private const string ENDPOINT_ENV = "INIA_TEST_S3";
    private const string TEST_ACCESS_KEY = "inia-test";
    private const string TEST_SECRET_KEY = "inia-test-secret";

    private ObjectStoreHarness(IAmazonS3 client, string bucket)
    {
        Client = client;
        Bucket = bucket;
    }

    public IAmazonS3 Client { get; }

    public string Bucket { get; }

    public static async Task<ObjectStoreHarness> StartOrSkipAsync(CancellationToken cancellationToken)
    {
        string? endpoint = Environment.GetEnvironmentVariable(ENDPOINT_ENV);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Assert.Skip($"Set {ENDPOINT_ENV} to the address of an S3-compatible store to run the object store tests.");
        }

        IAmazonS3 client = ObjectStorageClients.ForS3(new S3StorageSettings
        {
            ServiceUrl = endpoint,
            AccessKeyId = TEST_ACCESS_KEY,
            SecretAccessKey = TEST_SECRET_KEY,
        });
        string bucket = $"iniaai-test-{Guid.NewGuid():N}";
        _ = await client.PutBucketAsync(bucket, cancellationToken);
        return new ObjectStoreHarness(client, bucket);
    }

    public ObjectStorageService Store()
    {
        return new ObjectStorageService(Client, new SharedBucketLayout(Bucket));
    }

    public async Task<List<string>> ObjectKeysAsync()
    {
        ListObjectsV2Response page = await Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = Bucket });
        return [.. (page.S3Objects ?? []).Select(stored => stored.Key)];
    }

    public async ValueTask DisposeAsync()
    {
        foreach (string key in await ObjectKeysAsync())
        {
            _ = await Client.DeleteObjectAsync(Bucket, key);
        }

        _ = await Client.DeleteBucketAsync(Bucket);
        Client.Dispose();
    }
}
