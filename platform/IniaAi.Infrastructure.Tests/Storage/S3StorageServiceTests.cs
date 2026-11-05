using Amazon.Runtime;
using Amazon.S3;
using IniaAi.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Net;

namespace IniaAi.Infrastructure.Tests.Storage;

/// <summary>
/// A key S3 no longer holds reads as <see cref="FileNotFoundException"/>, as the database and MinIO
/// stores report it, so a caller can tell a file that is gone from a store that is failing. S3 is
/// the external boundary, so its client is substituted.
/// </summary>
public class S3StorageServiceTests
{
    private const string ORG_ID = "org-storage";
    private const string CATEGORY = "Documents";
    private const string PATH = "missing/report.pdf";

    private readonly IAmazonS3 _s3Client = Substitute.For<IAmazonS3>();
    private readonly S3StorageService _service;

    public S3StorageServiceTests()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"StorageBuckets:{CATEGORY}"] = "documents" })
            .Build();
        _service = new S3StorageService(_s3Client, configuration);
    }

    [Theory]
    [InlineData("NoSuchKey", HttpStatusCode.NotFound)]
    [InlineData("NotFound", HttpStatusCode.NotFound)]
    [InlineData("NoSuchKey", HttpStatusCode.BadRequest)]
    public async Task AKeyTheBucketDoesNotHold_IsAFileNotFound(string errorCode, HttpStatusCode statusCode)
    {
        _ = _s3Client.GetObjectAsync(Arg.Any<string>(), PATH, Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("The specified key does not exist.", ErrorType.Sender, errorCode, "request-1", statusCode));

        FileNotFoundException missing = await Assert.ThrowsAsync<FileNotFoundException>(() => _service.DownloadAsync(ORG_ID, CATEGORY, PATH));

        _ = Assert.IsType<AmazonS3Exception>(missing.InnerException);
    }

    [Fact]
    public async Task AStoreThatIsFailing_IsNotAFileNotFound()
    {
        _ = _s3Client.GetObjectAsync(Arg.Any<string>(), PATH, Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("Please reduce your request rate.", ErrorType.Receiver, "SlowDown", "request-2", HttpStatusCode.ServiceUnavailable));

        _ = await Assert.ThrowsAsync<AmazonS3Exception>(() => _service.DownloadAsync(ORG_ID, CATEGORY, PATH));
    }
}
