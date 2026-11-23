using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Storage;
using IniaAi.Testing.Fixtures;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Net;

namespace IniaAi.Infrastructure.Tests.Storage;

/// <summary>
/// Where each layout puts a file, and how the store reports what S3 answers. A key the store no
/// longer holds reads as <see cref="FileNotFoundException"/>, as the database store reports it, so
/// a caller can tell a file that is gone from a store that is failing. S3 is the external boundary,
/// so its client is substituted; the round trip against a real store is
/// <c>ObjectStorageOnObjectStoreTests</c>.
/// </summary>
public class ObjectStorageServiceTests
{
    private const string ORG_ID = "org-storage";
    private const string BUCKET = "iniaai-files";
    private const string KEY = "8b0e6f2a-0000-4000-8000-000000000001";

    private readonly IAmazonS3 _s3Client = Substitute.For<IAmazonS3>();

    [Theory]
    [InlineData("NoSuchKey", HttpStatusCode.NotFound)]
    [InlineData("NotFound", HttpStatusCode.NotFound)]
    [InlineData("NoSuchKey", HttpStatusCode.BadRequest)]
    [InlineData("NoSuchBucket", HttpStatusCode.BadRequest)]
    public async Task AKeyTheBucketDoesNotHold_IsAFileNotFound(string errorCode, HttpStatusCode statusCode)
    {
        _ = _s3Client.GetObjectAsync(BUCKET, $"{ORG_ID}/{KEY}", Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("The specified key does not exist.", ErrorType.Sender, errorCode, "request-1", statusCode));
        var service = new ObjectStorageService(_s3Client, new SharedBucketLayout(BUCKET));

        FileNotFoundException missing = await Assert.ThrowsAsync<FileNotFoundException>(() => service.DownloadAsync(ORG_ID, StorageCategories.Documents, KEY));

        _ = Assert.IsType<AmazonS3Exception>(missing.InnerException);
    }

    [Fact]
    public async Task AStoreThatIsFailing_IsNotAFileNotFound()
    {
        _ = _s3Client.GetObjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("Please reduce your request rate.", ErrorType.Receiver, "SlowDown", "request-2", HttpStatusCode.ServiceUnavailable));
        var service = new ObjectStorageService(_s3Client, new SharedBucketLayout(BUCKET));

        _ = await Assert.ThrowsAsync<AmazonS3Exception>(() => service.DownloadAsync(ORG_ID, StorageCategories.Documents, KEY));
    }

    [Theory]
    [InlineData(StorageCategories.Documents)]
    [InlineData(StorageCategories.Json2XlsxTemplates)]
    [InlineData(StorageCategories.AgentSessionBundles)]
    public async Task TheStacksBucket_KeepsEveryCategoryUnderTheOrganizationsPrefix_AndCreatesNoBucket(string category)
    {
        var service = new ObjectStorageService(_s3Client, new SharedBucketLayout(BUCKET));

        string stored = await service.UploadAsync(ORG_ID, category, KEY, new MemoryStream([1, 2, 3]), "application/pdf");

        Assert.Equal(KEY, stored);
        _ = await _s3Client.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(request => request.BucketName == BUCKET && request.Key == $"{ORG_ID}/{KEY}"),
            Arg.Any<CancellationToken>());
        _ = await _s3Client.DidNotReceiveWithAnyArgs().PutBucketAsync(default(string)!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheStacksBucket_DeletesTheFileWhateverCategoryTheCallerNames()
    {
        var service = new ObjectStorageService(_s3Client, new SharedBucketLayout(BUCKET));

        await service.DeleteAsync(ORG_ID, StorageCategories.Documents, KEY);

        _ = await _s3Client.Received(1).DeleteObjectAsync(BUCKET, $"{ORG_ID}/{KEY}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AnOrganizationIdHoldingTheSeparator_IsRefused()
    {
        var layout = new SharedBucketLayout(BUCKET);

        _ = Assert.Throws<ArgumentException>(() => layout.Locate("org/other", StorageCategories.Documents, KEY));
    }

    [Fact]
    public void TheStacksBucket_ReadsTheFileKeyOfAListedObjectPastItsPrefix()
    {
        IObjectLayout layout = new SharedBucketLayout(BUCKET);

        Assert.Equal(KEY, layout.KeyOf($"{ORG_ID}/{KEY}"));
        Assert.Equal("no-prefix", layout.KeyOf("no-prefix"));
    }

    [Fact]
    public async Task AStreamOfKnownSizeThatCannotSeek_IsSentByItsLength()
    {
        var service = new ObjectStorageService(_s3Client, new SharedBucketLayout(BUCKET));
        var content = new StoredObjectStream(new ForwardOnlyStream([1, 2, 3, 4, 5]), 5);

        _ = await service.UploadAsync(ORG_ID, StorageCategories.Documents, KEY, content, "application/pdf");

        _ = await _s3Client.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(request => request.InputStream == content && request.Headers.ContentLength == 5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStreamOfUnknownSize_IsRefused_ForTheManagerToBuffer()
    {
        var service = new ObjectStorageService(_s3Client, new SharedBucketLayout(BUCKET));

        _ = await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(ORG_ID, StorageCategories.Documents, KEY, new ForwardOnlyStream([1]), "application/pdf"));
    }

    [Fact]
    public async Task AFixedMinioBucket_KeepsTheBareKey()
    {
        var service = new ObjectStorageService(_s3Client, new FixedBucketLayout("inia-platform-files"));

        _ = await service.UploadAsync(ORG_ID, StorageCategories.Json2XlsxTemplates, KEY, new MemoryStream([1]), "application/octet-stream");

        _ = await _s3Client.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(request => request.BucketName == "inia-platform-files" && request.Key == KEY),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MinioBucketsPerOrganization_StayWhereTheyWere_AndADeleteClearsEveryCategory()
    {
        var layout = new BucketPerOrganizationLayout(new Dictionary<string, string> { [StorageCategories.Documents] = "docs" });
        var service = new ObjectStorageService(_s3Client, layout);

        _ = await service.UploadAsync("Org_1.A", StorageCategories.Documents, KEY, new MemoryStream([1]), "application/pdf");
        await service.DeleteAsync("Org_1.A", StorageCategories.Documents, KEY);

        _ = await _s3Client.Received(1).PutBucketAsync("org-1-a-docs", Arg.Any<CancellationToken>());
        _ = await _s3Client.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(request => request.BucketName == "org-1-a-docs" && request.Key == KEY),
            Arg.Any<CancellationToken>());
        _ = await _s3Client.Received(1).DeleteObjectAsync("org-1-a-docs", KEY, Arg.Any<CancellationToken>());
        _ = await _s3Client.Received(1).DeleteObjectAsync("org-1-a-json2xlsx-templates", KEY, Arg.Any<CancellationToken>());
        _ = await _s3Client.Received(1).DeleteObjectAsync("org-1-a-agent-session-bundles", KEY, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MinioBucketsPerOrganization_AreMadeOnce_AndARead_FindsAFileUnderAnotherCategory()
    {
        var service = new ObjectStorageService(_s3Client, new BucketPerOrganizationLayout(new Dictionary<string, string>()));
        _ = _s3Client.GetObjectAsync("org-documents", KEY, Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("The specified key does not exist.", ErrorType.Sender, "NoSuchKey", "request-3", HttpStatusCode.NotFound));
        _ = _s3Client.GetObjectAsync("org-json2xlsx-templates", KEY, Arg.Any<CancellationToken>())
            .Returns(new GetObjectResponse { ResponseStream = new MemoryStream([7, 7]), ContentLength = 2 });

        _ = await service.UploadAsync("org", StorageCategories.Documents, "first", new MemoryStream([1]), "application/pdf");
        _ = await service.UploadAsync("org", StorageCategories.Documents, "second", new MemoryStream([1]), "application/pdf");
        await using Stream read = await service.DownloadAsync("org", StorageCategories.Documents, KEY);

        _ = await _s3Client.Received(1).PutBucketAsync("org-documents", Arg.Any<CancellationToken>());
        Assert.Equal(2, read.Length);
    }
}
