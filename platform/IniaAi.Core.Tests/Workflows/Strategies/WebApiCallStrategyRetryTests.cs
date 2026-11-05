using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// The retry decision and the per-host concurrency gate of the WebApiCall block.
/// Every request is a genuine HttpClient round-trip against <see cref="LocalTestHttpServer"/>;
/// only the waits are intercepted, so a 3 s Retry-After is asserted on rather than slept.
/// </summary>
public class WebApiCallStrategyRetryTests
    : IClassFixture<WorkflowsFixture<WebApiCallStrategyRetryTests>>, IClassFixture<LocalTestHttpServer>
{
    private const string AUTHORIZATION_HEADER = "Authorization";
    private const int TEST_TRANSIENT_DELAY_MS = 7;
    private const int TEST_RATE_LIMIT_MIN_DELAY_MS = 100;
    private const int TEST_RATE_LIMIT_BASE_DELAY_MS = 1000;
    private const int TEST_RATE_LIMIT_MAX_DELAY_MS = 4000;
    private const int TEST_RATE_LIMIT_CEILING_MS = 120000;
    private const int TEST_RATE_LIMIT_MAX_RETRIES = 3;

    private readonly IServiceProvider _testServiceProvider;
    private readonly LocalTestHttpServer _server;

    public WebApiCallStrategyRetryTests(
        WorkflowsFixture<WebApiCallStrategyRetryTests> fixture,
        LocalTestHttpServer server,
        ITestOutputHelper outputHelper)
    {
        fixture.PlatformSettings.OutboundAccess.AllowedInternalHosts = ["127.0.0.1"];
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
        _server = server;
    }

    /// <summary>
    /// Records every backoff wait instead of sleeping it, shortens the transient schedule to
    /// milliseconds, and shrinks the 429 schedule's retry count so exhaustion is reachable.
    /// The jitter window and budget keep production-like proportions so the delays recorded
    /// can be checked against them.
    /// </summary>
    private class RecordingWebApiCallStrategy : WebApiCallStrategy
    {
        public List<TimeSpan> Delays { get; } = [];

        protected override IReadOnlyList<int> RetryDelaysMs { get; } =
            [TEST_TRANSIENT_DELAY_MS, TEST_TRANSIENT_DELAY_MS, TEST_TRANSIENT_DELAY_MS, TEST_TRANSIENT_DELAY_MS, TEST_TRANSIENT_DELAY_MS];

        protected override WebApiCallRateLimitSchedule RateLimitSchedule { get; } = new(
            TEST_RATE_LIMIT_MAX_RETRIES,
            TEST_RATE_LIMIT_MIN_DELAY_MS,
            TEST_RATE_LIMIT_BASE_DELAY_MS,
            TEST_RATE_LIMIT_MAX_DELAY_MS,
            TEST_RATE_LIMIT_CEILING_MS);

        protected override Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class ShortTimeoutWebApiCallStrategy : RecordingWebApiCallStrategy
    {
        protected override TimeSpan RequestTimeout => TimeSpan.FromMilliseconds(200);
    }

    private sealed class TightBudgetWebApiCallStrategy : RecordingWebApiCallStrategy
    {
        private const int TIGHT_CEILING_MS = 5000;

        protected override WebApiCallRateLimitSchedule RateLimitSchedule { get; } = new(
            TEST_RATE_LIMIT_MAX_RETRIES,
            TEST_RATE_LIMIT_MIN_DELAY_MS,
            TEST_RATE_LIMIT_BASE_DELAY_MS,
            TEST_RATE_LIMIT_MAX_DELAY_MS,
            TIGHT_CEILING_MS);
    }

    /// <summary>
    /// Owns a private gate and a private limit so the concurrency tests neither share the
    /// process-wide gate with other test classes nor mutate the fixture's PlatformSettings.
    /// </summary>
    private sealed class GatedWebApiCallStrategy(int limit) : WebApiCallStrategy
    {
        private readonly WebApiCallSettings _settings = new() { MaxConcurrentRequestsPerHost = limit };

        protected override WebApiCallHostGate HostGate { get; } = new();

        protected override WebApiCallSettings ResolveSettings(BlockExecutionContext context)
        {
            return _settings;
        }
    }

    private (Block Block, BlockExecutionContext Context) Arrange(WebApiCallProperties properties)
    {
        var block = new Block(BlockTypes.WebApiCall, new Section("test", new Workflow("test")))
        {
            Name = "Test",
            Properties = properties,
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        block.SetExecutionState(false, []);

        return (block, context);
    }

    private static string UniqueKey(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    // The strategy formats seconds with the invariant culture; so must the expectation.
    private static string Seconds(double seconds)
    {
        return seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task RateLimited_WithRetryAfterSeconds_WaitsExactlyThatLongThenSucceedsAsync()
    {
        string key = UniqueKey("ra-seconds");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/429?key={key}&times=1&retryAfter=3",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Equal([TimeSpan.FromSeconds(3)], strategy.Delays);
        Assert.Equal(2, _server.StatusHits(key));
    }

    [Fact]
    public async Task RateLimited_WithRetryAfterHttpDate_WaitsUntilThatDateAsync()
    {
        string key = UniqueKey("ra-date");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/429?key={key}&times=1&retryAfterDateSeconds=20",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        TimeSpan delay = Assert.Single(strategy.Delays);
        Assert.True(delay > TimeSpan.FromSeconds(15) && delay <= TimeSpan.FromSeconds(20),
            $"An HTTP-date 20s ahead should wait roughly 20s (allowing for the round trip), waited {delay}");
    }

    [Fact]
    public async Task RateLimited_WithoutRetryAfter_FollowsJitteredScheduleThenSucceedsAsync()
    {
        string key = UniqueKey("ra-none");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/429?key={key}&times=2",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(2, strategy.Delays.Count);
        Assert.Equal(3, _server.StatusHits(key));

        for (int retry = 0; retry < strategy.Delays.Count; retry++)
        {
            double cap = Math.Min(TEST_RATE_LIMIT_MAX_DELAY_MS, TEST_RATE_LIMIT_BASE_DELAY_MS * Math.Pow(2, retry));
            double delayMs = strategy.Delays[retry].TotalMilliseconds;
            Assert.True(delayMs >= TEST_RATE_LIMIT_MIN_DELAY_MS && delayMs <= cap,
                $"Retry {retry} waited {delayMs}ms, outside the jitter window [{TEST_RATE_LIMIT_MIN_DELAY_MS}, {cap}]");
        }
    }

    [Fact]
    public async Task RateLimited_PastRetryCount_FailsNamingStatusAttemptsAndTotalWaitAsync()
    {
        string key = UniqueKey("ra-exhausted");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/429?key={key}",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        int expectedAttempts = TEST_RATE_LIMIT_MAX_RETRIES + 1;
        double waitedSeconds = strategy.Delays.Sum(d => d.TotalMilliseconds) / 1000d;
        Assert.Equal(TEST_RATE_LIMIT_MAX_RETRIES, strategy.Delays.Count);
        Assert.Equal(expectedAttempts, _server.StatusHits(key));
        Assert.Contains("HTTP 429", exception.Message);
        Assert.Contains($"after {expectedAttempts} attempts", exception.Message);
        Assert.Contains($"{Seconds(waitedSeconds)}s of backoff", exception.Message);
        Assert.Contains("rate-limit retries are used up", exception.Message);
        Assert.Equal(HttpStatusCode.TooManyRequests, exception.Data["StatusCode"]);
    }

    [Fact]
    public async Task RateLimited_RetryAfterBeyondBudget_FailsWithoutWaitingAsync()
    {
        string key = UniqueKey("ra-over-budget");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/429?key={key}&retryAfter=10",
            HttpMethod = "GET",
        });
        var strategy = new TightBudgetWebApiCallStrategy();

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Empty(strategy.Delays);
        Assert.Equal(1, _server.StatusHits(key));
        Assert.Contains("HTTP 429", exception.Message);
        Assert.Contains("after 1 attempt and", exception.Message);
        Assert.Contains("Retry-After asks for 10s", exception.Message);
        Assert.Contains("5s backoff budget", exception.Message);
    }

    [Theory]
    [InlineData("retryAfter=soon")]
    [InlineData("retryAfter=-5")]
    [InlineData("retryAfter=0")]
    [InlineData("retryAfterDateSeconds=-30")]
    public async Task RateLimited_UnusableRetryAfter_FallsBackToScheduleAsync(string retryAfterQuery)
    {
        string key = UniqueKey("ra-unusable");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/429?key={key}&times=1&{retryAfterQuery}",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        TimeSpan delay = Assert.Single(strategy.Delays);
        Assert.True(
            delay.TotalMilliseconds >= TEST_RATE_LIMIT_MIN_DELAY_MS
            && delay.TotalMilliseconds <= TEST_RATE_LIMIT_BASE_DELAY_MS,
            $"An unusable Retry-After ({retryAfterQuery}) must fall back to the first jitter window, waited {delay}");
    }

    [Fact]
    public async Task RateLimited_HugeRetryAfter_IsCappedAsync()
    {
        string key = UniqueKey("ra-huge");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/429?key={key}&times=1&retryAfter=86400",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(120)], strategy.Delays);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task ClientError_FailsOnFirstAttemptWithoutWaitingAsync(int statusCode)
    {
        string key = UniqueKey("fail-fast");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/{statusCode}?key={key}&retryAfter=3",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Empty(strategy.Delays);
        Assert.Equal(1, _server.StatusHits(key));
        Assert.Contains($"HTTP {statusCode}", exception.Message);
    }

    [Fact]
    public async Task ServerError_StillRetriesOnTransientScheduleThenSucceedsAsync()
    {
        string key = UniqueKey("transient-500");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/500?key={key}&times=2&retryAfter=30",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(3, _server.StatusHits(key));
        Assert.Equal(
            [TimeSpan.FromMilliseconds(TEST_TRANSIENT_DELAY_MS), TimeSpan.FromMilliseconds(TEST_TRANSIENT_DELAY_MS)],
            strategy.Delays);
    }

    [Fact]
    public async Task ServerError_PastRetryCount_FailsNamingStatusAttemptsAndTotalWaitAsync()
    {
        string key = UniqueKey("transient-exhausted");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/503?key={key}",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(5, strategy.Delays.Count);
        Assert.Equal(6, _server.StatusHits(key));
        Assert.Contains("HTTP 503", exception.Message);
        Assert.Contains("after 6 attempts", exception.Message);
        Assert.Contains($"{Seconds(5 * TEST_TRANSIENT_DELAY_MS / 1000d)}s of backoff", exception.Message);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.Data["StatusCode"]);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    public async Task TransientClientStatus_RetriesOnTransientScheduleAsync(int statusCode)
    {
        string key = UniqueKey("transient-4xx");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/{statusCode}?key={key}&times=1",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(2, _server.StatusHits(key));
        Assert.Equal([TimeSpan.FromMilliseconds(TEST_TRANSIENT_DELAY_MS)], strategy.Delays);
    }

    [Fact]
    public async Task RateLimitedThenServerError_TracksTheTwoSchedulesSeparatelyAsync()
    {
        string key = UniqueKey("mixed");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/429?key={key}&statuses=429,503,429&retryAfter=2",
            HttpMethod = "GET",
        });
        var strategy = new RecordingWebApiCallStrategy();

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(4, _server.StatusHits(key));
        Assert.Equal(
            [TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(TEST_TRANSIENT_DELAY_MS), TimeSpan.FromSeconds(2)],
            strategy.Delays);
    }

    [Fact]
    public async Task RequestTimeout_PastRetryCount_FailsNamingHostAttemptsAndTotalWaitAsync()
    {
        string key = UniqueKey("timeout");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/delay?ms=3000&key={key}&secret=do-not-leak",
            HttpMethod = "GET",
        });
        var strategy = new ShortTimeoutWebApiCallStrategy();

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(5, strategy.Delays.Count);
        Assert.Contains("timed out", exception.Message);
        Assert.Contains("on attempt 6", exception.Message);
        Assert.Contains($"{Seconds(5 * TEST_TRANSIENT_DELAY_MS / 1000d)}s of backoff", exception.Message);
        Assert.DoesNotContain("do-not-leak", exception.Message);
    }

    [Fact]
    public async Task ConcurrencyGate_DropsAKeyOnceItsLastHolderLeavesAsync()
    {
        const int PARALLEL = 4;
        string key = UniqueKey("gate-evict");
        var gate = new WebApiCallHostGate();
        List<Task> holders = [];

        for (int i = 0; i < PARALLEL; i++)
        {
            holders.Add(Task.Run(async () =>
            {
                using IDisposable slot = await gate.AcquireAsync(key, 2, TestContext.Current.CancellationToken);
                await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(holders);

        Assert.Equal(0, gate.ActiveKeys);

        using IDisposable again = await gate.AcquireAsync(key, 2, TestContext.Current.CancellationToken);
        Assert.Equal(1, gate.ActiveKeys);
    }

    [Fact]
    public async Task ConcurrencyGate_CancelledWaitLeavesNoMemberBehindAsync()
    {
        string key = UniqueKey("gate-cancel");
        var gate = new WebApiCallHostGate();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        IDisposable held = await gate.AcquireAsync(key, 1, TestContext.Current.CancellationToken);
        Task<IDisposable> waiting = gate.AcquireAsync(key, 1, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(1, gate.ActiveKeys);

        held.Dispose();

        Assert.Equal(0, gate.ActiveKeys);
    }

    [Fact]
    public async Task ConcurrencyGate_LimitsParallelBlocksToConfiguredDegreeAsync()
    {
        const int LIMIT = 2;
        const int BLOCKS = 6;
        string key = UniqueKey("gate-limited");
        var strategy = new GatedWebApiCallStrategy(LIMIT);

        List<Task> runs = [];

        for (int i = 0; i < BLOCKS; i++)
        {
            (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
            {
                UrlTemplate = $"{_server.BaseUrl}/inflight?key={key}&holdMs=300",
                HttpMethod = "GET",
                AdditionalHeaders = new Dictionary<string, string>
                {
                    { AUTHORIZATION_HEADER, $"Bearer {key}" },
                },
            });
            runs.Add(strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(runs);

        Assert.Equal(LIMIT, _server.MaxInFlight(key));
    }

    [Fact]
    public async Task ConcurrencyGate_WithoutALowLimit_LetsBlocksStampedeAsync()
    {
        const int BLOCKS = 6;
        string key = UniqueKey("gate-open");
        var strategy = new GatedWebApiCallStrategy(BLOCKS);

        List<Task> runs = [];

        for (int i = 0; i < BLOCKS; i++)
        {
            (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
            {
                UrlTemplate = $"{_server.BaseUrl}/inflight?key={key}&holdMs=300",
                HttpMethod = "GET",
                AdditionalHeaders = new Dictionary<string, string>
                {
                    { AUTHORIZATION_HEADER, $"Bearer {key}" },
                },
            });
            runs.Add(strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(runs);

        Assert.True(_server.MaxInFlight(key) > 2,
            $"With the limit lifted the six blocks should overlap beyond two; saw {_server.MaxInFlight(key)}");
    }

    [Fact]
    public async Task ConcurrencyGate_ReleasesSlotWhenTheRequestFailsAsync()
    {
        const int LIMIT = 1;
        string key = UniqueKey("gate-release");
        var strategy = new GatedWebApiCallStrategy(LIMIT);
        (Block failing, BlockExecutionContext failingContext) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/400?key={key}",
            HttpMethod = "GET",
        });
        (Block succeeding, BlockExecutionContext succeedingContext) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/inflight?key={key}&holdMs=50",
            HttpMethod = "GET",
        });

        await Assert.ThrowsAsync<HttpRequestException>(
            () => strategy.ExecuteBlockAsync(failing, failingContext, TestContext.Current.CancellationToken));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _ = await strategy.ExecuteBlockAsync(succeeding, succeedingContext, timeout.Token);

        Assert.NotNull(succeeding.BlockResponse);
    }

    [Fact]
    public void GateKey_SeparatesCredentialsAndNeverContainsThem()
    {
        const string HOST = "API.Perplexity.ai";
        var orgA = new Dictionary<string, string> { { "authorization", "Bearer pplx-org-a-secret" } };
        var orgB = new Dictionary<string, string> { { "Authorization", "Bearer pplx-org-b-secret" } };
        var anonymous = new Dictionary<string, string> { { "Accept", "application/json" } };
        var apiKey = new Dictionary<string, string> { { "X-API-Key", "abc123" } };

        string keyA = WebApiCallHostGate.BuildKey(HOST, orgA);
        string keyB = WebApiCallHostGate.BuildKey(HOST, orgB);
        string keyAnonymous = WebApiCallHostGate.BuildKey(HOST, anonymous);
        string keyApiKey = WebApiCallHostGate.BuildKey(HOST, apiKey);

        Assert.NotEqual(keyA, keyB);
        Assert.NotEqual(keyA, keyAnonymous);
        Assert.NotEqual(keyApiKey, keyAnonymous);
        Assert.Equal("api.perplexity.ai", keyAnonymous);
        Assert.StartsWith("api.perplexity.ai:", keyA);
        Assert.DoesNotContain("secret", keyA);
        Assert.DoesNotContain("abc123", keyApiKey);
        Assert.Equal(keyA, WebApiCallHostGate.BuildKey(HOST.ToLowerInvariant(), orgA));
    }

    [Fact]
    public void Settings_ResolveLimit_PrefersHostOverrideAndSeedsPerplexity()
    {
        var settings = new WebApiCallSettings();

        Assert.Equal(WebApiCallSettings.PERPLEXITY_MAX_CONCURRENT_REQUESTS, settings.ResolveMaxConcurrentRequests("API.PERPLEXITY.AI"));
        Assert.Equal(WebApiCallSettings.DEFAULT_MAX_CONCURRENT_REQUESTS_PER_HOST, settings.ResolveMaxConcurrentRequests("api.example.com"));

        settings.MaxConcurrentRequestsPerHost = 0;
        settings.MaxConcurrentRequestsByHost["api.example.com"] = 5;

        Assert.Equal(5, settings.ResolveMaxConcurrentRequests("api.example.com"));
        Assert.Equal(1, settings.ResolveMaxConcurrentRequests("other.example.com"));
    }

    [Theory]
    [InlineData("3", 3d)]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    [InlineData("soon", null)]
    [InlineData("86400", 120d)]
    public void ParseRetryAfter_DeltaForms(string headerValue, double? expectedSeconds)
    {
        RetryConditionHeaderValue? header = RetryConditionHeaderValue.TryParse(headerValue, out RetryConditionHeaderValue? parsed)
            ? parsed
            : null;

        TimeSpan? wait = WebApiCallStrategy.ParseRetryAfter(header, DateTimeOffset.UtcNow);

        Assert.Equal(expectedSeconds is null ? null : TimeSpan.FromSeconds(expectedSeconds.Value), wait);
    }

    [Fact]
    public void ParseRetryAfter_DateForms()
    {
        var now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(45), WebApiCallStrategy.ParseRetryAfter(new RetryConditionHeaderValue(now.AddSeconds(45)), now));
        Assert.Null(WebApiCallStrategy.ParseRetryAfter(new RetryConditionHeaderValue(now.AddSeconds(-45)), now));
        Assert.Null(WebApiCallStrategy.ParseRetryAfter(new RetryConditionHeaderValue(now), now));
        Assert.Equal(TimeSpan.FromSeconds(120), WebApiCallStrategy.ParseRetryAfter(new RetryConditionHeaderValue(now.AddHours(2)), now));
        Assert.Null(WebApiCallStrategy.ParseRetryAfter(null, now));
    }
}
