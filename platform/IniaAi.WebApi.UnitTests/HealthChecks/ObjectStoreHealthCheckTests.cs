using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using IniaAi.Infrastructure.Storage;
using IniaAi.Testing;
using IniaAi.WebApi.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Net;

namespace IniaAi.WebApi.UnitTests.HealthChecks;

/// <summary>
/// The load balancer probes the health endpoint every 30 seconds on each host, so the check asks the
/// store at most once a minute, and a store that fails reports the registration's status instead of
/// throwing. S3 is the external boundary, so its client is substituted.
/// </summary>
public class ObjectStoreHealthCheckTests
{
    private const string BUCKET = "iniaai-files";

    private readonly IAmazonS3 _s3Client = Substitute.For<IAmazonS3>();
    private readonly SettableClock _clock = new(DateTimeOffset.UtcNow);
    private readonly ObjectStoreHealthCheck _check;

    public ObjectStoreHealthCheckTests()
    {
        _check = new ObjectStoreHealthCheck(new ObjectStorageService(_s3Client, new SharedBucketLayout(BUCKET)), _clock);
    }

    [Fact]
    public async Task AStoreThatAnswers_IsHealthy_AndIsAskedAgainOnlyAfterTheAnswerGoesStale()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        HealthCheckResult first = await _check.CheckHealthAsync(Context(), cancellationToken);
        _clock.Advance(ObjectStoreHealthCheck.Freshness - TimeSpan.FromSeconds(1));
        _ = await _check.CheckHealthAsync(Context(), cancellationToken);

        Assert.Equal(HealthStatus.Healthy, first.Status);
        _ = await _s3Client.Received(1).ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(request => request.BucketName == BUCKET && request.MaxKeys == 1), Arg.Any<CancellationToken>());

        _clock.Advance(TimeSpan.FromSeconds(1));
        _ = await _check.CheckHealthAsync(Context(), cancellationToken);

        _ = await _s3Client.Received(2).ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreThatFails_ReportsTheRegistrationsStatus_AndKeepsThatAnswerAsLong()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        _ = _s3Client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AmazonS3Exception("Access Denied", ErrorType.Sender, "AccessDenied", "request-1", HttpStatusCode.Forbidden));

        HealthCheckResult failed = await _check.CheckHealthAsync(Context(), cancellationToken);
        HealthCheckResult again = await _check.CheckHealthAsync(Context(), cancellationToken);

        Assert.Equal(HealthStatus.Degraded, failed.Status);
        _ = Assert.IsType<AmazonS3Exception>(failed.Exception);
        Assert.Equal(HealthStatus.Degraded, again.Status);
        _ = await _s3Client.Received(1).ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreThatTimesOut_ReportsTheRegistrationsStatus_AndIsNotAskedAgainOnTheNextProbe()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        _ = _s3Client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request timed out."));

        HealthCheckResult failed = await _check.CheckHealthAsync(Context(), cancellationToken);
        _ = await _check.CheckHealthAsync(Context(), cancellationToken);

        Assert.Equal(HealthStatus.Degraded, failed.Status);
        _ = await _s3Client.Received(1).ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AProbeThatIsCancelled_IsCancelled_AndLeavesNoAnswer()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _ = _s3Client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<ListObjectsV2Response>(new OperationCanceledException(cancelled.Token)),
                _ => Task.FromResult(new ListObjectsV2Response()));

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _check.CheckHealthAsync(Context(), cancelled.Token));
        HealthCheckResult next = await _check.CheckHealthAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, next.Status);
    }

    private HealthCheckContext Context()
    {
        return new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(ObjectStoreHealthCheck.S3_NAME, _check, HealthStatus.Degraded, null),
        };
    }
}
