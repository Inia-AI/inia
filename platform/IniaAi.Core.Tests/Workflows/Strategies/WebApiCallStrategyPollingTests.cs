using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// Tests for the opt-in poll-until-condition mode of the WebApiCall block.
/// HTTP is stubbed by overriding <see cref="WebApiCallStrategy.ExecuteRequestAsync"/>
/// (the same seam the Perplexity tests use). The inter-poll wait is faked by
/// overriding <c>DelayBetweenPollsAsync</c> to advance a virtual clock instead of
/// sleeping, so the whole suite is instant and deterministic.
/// </summary>
public class WebApiCallStrategyPollingTests : IClassFixture<WorkflowsFixture<WebApiCallStrategyPollingTests>>
{
    private const string STATUS_URL = "https://api.example.com/v1/tasks/runs/abc/status";
    private const string COMPLETION_URL = "https://api.example.com/v1/tasks/runs/abc/result";
    private const string PARALLEL_AI_RESULT_URL = "https://api.parallel.ai/v1/tasks/runs/trun_123/result";

    private const string PARALLEL_AI_RESULT_BODY = """
        {
            "run": { "status": "completed" },
            "output": {
                "type": "json",
                "content": {
                    "executive_summary": "Total revenue was $12,278 million in 2024."
                },
                "basis": [
                    {
                        "field": "executive_summary",
                        "citations": [
                            { "title": "T1", "url": "https://example.com/report", "excerpts": ["Revenue: $12,278 million"] }
                        ],
                        "reasoning": "r",
                        "confidence": "high"
                    }
                ]
            }
        }
        """;

    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;

    public WebApiCallStrategyPollingTests(
        WorkflowsFixture<WebApiCallStrategyPollingTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _testServiceProvider = fixture.BuildTestServiceProvider(_outputHelper);
    }

    /// <summary>
    /// Minimal TimeProvider whose clock only moves when explicitly advanced.
    /// Mirrors the hand-rolled fake used in IniaAi.AiConnection.Tests.
    /// </summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }

    /// <summary>
    /// Returns a queued sequence of canned bodies (the last entry repeats once
    /// exhausted, so timeout/never-completes tests can spin). No real waiting:
    /// the delay override advances the virtual clock by the requested interval and
    /// can optionally cancel a token to simulate cancellation mid-poll.
    /// </summary>
    private static HttpRequestException HttpError(HttpStatusCode code)
    {
        var ex = new HttpRequestException($"HTTP {(int)code} {code}");
        ex.Data["StatusCode"] = code;
        return ex;
    }

    private sealed class PollingStubStrategy : WebApiCallStrategy
    {
        // Each step is either a response body (string) or an exception to throw for that poll.
        private readonly Queue<object> _steps;
        private readonly TestTimeProvider _clock;
        private readonly CancellationTokenSource? _cancelOnDelay;

        public int CallCount { get; private set; }
        public int DelayCount { get; private set; }
        public int LastDelayMs { get; private set; }
        public string? LastMethod { get; private set; }
        public List<string> RequestedUrls { get; } = [];

        public PollingStubStrategy(
            TestTimeProvider clock,
            IEnumerable<object> steps,
            CancellationTokenSource? cancelOnDelay = null)
            : base(clock)
        {
            _clock = clock;
            _steps = new Queue<object>(steps);
            _cancelOnDelay = cancelOnDelay;
        }

        protected override Task<string> ExecuteRequestAsync(
            HttpMessageHandler handler,
            string url,
            string method,
            string? payload,
            Dictionary<string, string> headers,
            string contentType,
            string acceptHeader,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastMethod = method;
            RequestedUrls.Add(url);
            object step = _steps.Count > 1
                ? _steps.Dequeue()
                : _steps.Peek();
            if (step is Exception ex)
            {
                return Task.FromException<string>(ex);
            }

            return Task.FromResult((string)step);
        }

        protected override Task DelayBetweenPollsAsync(int milliseconds, CancellationToken cancellationToken)
        {
            DelayCount++;
            LastDelayMs = milliseconds;
            _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
            _cancelOnDelay?.Cancel();
            return Task.CompletedTask;
        }
    }

    private (Block block, BlockExecutionContext context) SetupBlock(
        string? jsonPath,
        string? successValues,
        string? failureValues = null,
        int? intervalSeconds = null,
        int? timeoutSeconds = null,
        string httpMethod = "GET",
        int? delayAfterRequestSeconds = null,
        string? completionUrlTemplate = null)
    {
        var workflow = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.WebApiCall, workflow)
        {
            Name = "Polling Call",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = STATUS_URL,
                HttpMethod = httpMethod,
                PollUntilJsonPath = jsonPath,
                PollUntilSuccessValues = successValues,
                PollUntilFailureValues = failureValues,
                PollIntervalSeconds = intervalSeconds,
                PollTimeoutSeconds = timeoutSeconds,
                DelayAfterRequestSeconds = delayAfterRequestSeconds,
                PollCompletionUrlTemplate = completionUrlTemplate,
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        block.SetExecutionState(false, []);
        return (block, context);
    }

    private static string Status(string value)
    {
        return $$"""{"status":"{{value}}"}""";
    }

    // ---- Poll loop ---------------------------------------------------------

    [Fact]
    public async Task Poll_PendingThenCompleted_ReturnsCompletedResponseAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        string completedBody = Status("completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("pending"), Status("pending"), completedBody]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(3, strategy.CallCount);
        Assert.Equal(2, strategy.DelayCount);
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(completedBody, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task Poll_FirstPollAlreadyComplete_ReturnsImmediatelyAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed,succeeded");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(1, strategy.CallCount);
        Assert.Equal(0, strategy.DelayCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_FailureValue_FailsFastWithoutWaitingOutTimeoutAsync()
    {
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", failureValues: "failed,error,cancelled");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("failed")]);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(1, strategy.CallCount);
        Assert.Equal(0, strategy.DelayCount);
        Assert.Contains("failed", ex.Message);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("CANCELLED")]
    public async Task Poll_AnyFailureValueCaseInsensitive_FailsFastAsync(string status)
    {
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", failureValues: "failed,error,cancelled");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status(status)]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(1, strategy.CallCount);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("SUCCEEDED")]
    public async Task Poll_SuccessValueCaseInsensitive_CompletesAsync(string status)
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed,succeeded");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status(status)]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(1, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_NeverReachesSuccess_ThrowsTimeoutNamingLastStatusAsync()
    {
        // interval 5s, timeout 10s -> trips on the 3rd observed status.
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", intervalSeconds: 5, timeoutSeconds: 10);
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("pending")]);

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("pending", ex.Message);
        Assert.Contains("timed out", ex.Message);
        Assert.True(strategy.CallCount >= 2, $"Expected multiple polls, got {strategy.CallCount}");
    }

    [Fact]
    public async Task SingleRequest_BodyContainsNullCharacters_StoredResponsesAreCleanAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(null, null);
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            ["{\"data\":\"val" + '\0' + "ue\"}"]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.DoesNotContain('\0', block.BlockResponse.RawResponse!);
        Assert.DoesNotContain('\0', block.BlockResponse.JsonResponse!);
    }

    [Fact]
    public async Task Poll_StatusContainsNullCharacter_TimeoutMessageIsCleanAsync()
    {
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", intervalSeconds: 5, timeoutSeconds: 10);
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("pen" + '\0' + "ding")]);

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.DoesNotContain('\0', ex.Message);
        Assert.Contains("pending", ex.Message);
    }

    [Fact]
    public async Task Poll_NumericStatus_MatchesStringifiedSuccessValueAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "3");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), ["""{"status":3}"""]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(1, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_BooleanStatus_MatchesSuccessValueAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("done", "true");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), ["""{"done":true}"""]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(1, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_MissingPathOrNonJson_KeepsPollingUntilSuccessAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            ["""{"other":"x"}""", "not json at all", Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(3, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_NestedPath_IsResolvedAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("run.status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            ["""{"run":{"status":"completed"}}"""]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(1, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_ObjectOrArrayLeaf_KeepsPollingUntilPrimitiveAsync()
    {
        // A path that resolves to an object then an array must count as "not met"
        // (only primitive leaves can match), so polling continues to the completed body.
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            ["""{"status":{"nested":"x"}}""", """{"status":["a","b"]}""", Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(3, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_CancelledMidWait_PropagatesCancellationAsync()
    {
        using var cts = new CancellationTokenSource();
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("pending")],
            cancelOnDelay: cts);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => strategy.ExecuteBlockAsync(block, context, cts.Token));

        // One poll, then the wait cancelled the token; the next loop iteration observed it.
        Assert.Equal(1, strategy.CallCount);
    }

    [Fact]
    public async Task Poll_EnabledButNoSuccessValues_FailsFastWithoutAnyHttpCallAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", successValues: "   ");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("completed")]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(0, strategy.CallCount);
    }

    [Fact]
    public async Task Poll_IntervalClampedToMinimum_UsesFloorAsync()
    {
        // Ask for 1s; minimum is 5s. The delay override records the ms actually requested.
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", intervalSeconds: 1, timeoutSeconds: 600);
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("pending"), Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.True(strategy.LastDelayMs >= 5000,
            $"Interval should clamp up to >= 5000ms, was {strategy.LastDelayMs}ms");
    }

    [Fact]
    public async Task Poll_HonoursConfiguredMethod_DefaultGetAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal("GET", strategy.LastMethod);
    }

    [Fact]
    public async Task Poll_SuccessAndFailureValuesOverlap_FailsFastWithoutAnyHttpCallAsync()
    {
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", failureValues: "completed,error");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("completed")]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(0, strategy.CallCount);
    }

    [Fact]
    public async Task Poll_RootLevelJsonArray_KeepsPollingAsync()
    {
        // A non-object root cannot resolve a dot-path, so it counts as "not met".
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            ["""[{"status":"completed"}]""", Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(2, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_WhitespaceAndTrailingCommasInSuccessValues_StillMatchesAsync()
    {
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "  completed , succeeded ,  ");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(1, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_TimeoutAboveMaximum_ClampsDownToCeilingAsync()
    {
        // 99999s requested; ceiling is 3600s. With a 5s interval that is ~720 polls,
        // not ~20000 - so a bounded CallCount proves the clamp engaged.
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", intervalSeconds: 5, timeoutSeconds: 99999);
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("pending")]);

        _ = await Assert.ThrowsAsync<TimeoutException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.True(strategy.CallCount is > 700 and < 740,
            $"Expected ~720 polls (3600s/5s) proving clamp to ceiling, got {strategy.CallCount}");
    }

    [Fact]
    public async Task Poll_TimeoutBelowInterval_ClampsUpToIntervalAndPollsAtLeastOnceAsync()
    {
        // timeout 3s < interval 10s -> clamped up to 10s, so at least one poll runs.
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", intervalSeconds: 10, timeoutSeconds: 3);
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("pending")]);

        _ = await Assert.ThrowsAsync<TimeoutException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(1, strategy.CallCount);
    }

    // ---- Transient error handling -----------------------------------------

    [Fact]
    public async Task Poll_TransientServerError_ToleratedThenSucceedsAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [HttpError(HttpStatusCode.ServiceUnavailable), Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(2, strategy.CallCount);
        Assert.Equal(1, strategy.DelayCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_PerRequestTimeout_ToleratedThenSucceedsAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [new TaskCanceledException("request timed out", new TimeoutException()), Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(2, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Poll_NonRetryableClientError_FailsFastAsync(HttpStatusCode code)
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [HttpError(code)]);

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(1, strategy.CallCount);
        Assert.Equal(0, strategy.DelayCount);
        Assert.Contains(((int)code).ToString(), ex.Message);
    }

    [Fact]
    public async Task Poll_ConsecutiveErrorsHitCap_FailsWithLastErrorAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [HttpError(HttpStatusCode.ServiceUnavailable)]);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(5, strategy.CallCount);
        Assert.Contains("consecutive", ex.Message);
        Assert.Contains("503", ex.Message);
    }

    [Fact]
    public async Task Poll_TimeoutAfterTransientErrors_NamesLastErrorAsync()
    {
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", intervalSeconds: 5, timeoutSeconds: 5);
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [HttpError(HttpStatusCode.BadGateway)]);

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("timed out", ex.Message);
        Assert.Contains("last error", ex.Message);
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public async Task Poll_ErrorsResetOnSuccessfulPoll_DoNotHitCumulativeCapAsync()
    {
        // 4 errors, a good (not-yet-met) poll resets the counter, 4 more errors,
        // then completed. Cumulative errors (8) exceed the cap of 5, but the max
        // consecutive run (4) never does, so it must succeed.
        var steps = new List<object>();
        for (int i = 0; i < 4; i++)
        {
            steps.Add(HttpError(HttpStatusCode.ServiceUnavailable));
        }

        steps.Add(Status("pending"));
        for (int i = 0; i < 4; i++)
        {
            steps.Add(HttpError(HttpStatusCode.ServiceUnavailable));
        }

        steps.Add(Status("completed"));

        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), steps);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(10, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    [Fact]
    public async Task Poll_CancellationDuringRequest_PropagatesNotSwallowedAsync()
    {
        // A cancelled request (TaskCanceledException with no inner TimeoutException)
        // is workflow cancellation, not a per-request timeout, so it must propagate
        // rather than be tolerated as transient or converted to a TimeoutException.
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [new TaskCanceledException()]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(1, strategy.CallCount);
        Assert.Equal(0, strategy.DelayCount);
    }

    [Fact]
    public async Task Poll_NonRetryableClientErrorMidStream_FailsFastAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("pending"), Status("pending"), HttpError(HttpStatusCode.Unauthorized)]);

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(3, strategy.CallCount);
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Poll_FailureStatusMidStream_FailsFastAsync()
    {
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", failureValues: "failed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("pending"), Status("pending"), Status("failed")]);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(3, strategy.CallCount);
        Assert.Contains("failed", ex.Message);
    }

    [Fact]
    public async Task Poll_TransientErrorAndPendingInterleaved_SucceedsAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("status", "completed");
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [HttpError(HttpStatusCode.ServiceUnavailable), Status("pending"), Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(3, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }

    // ---- Completion action --------------------------------------------------

    [Fact]
    public async Task PollThenAction_FetchesCompletionUrlAndReturnsItsResponseAsync()
    {
        const string RESULT_BODY = """{"answer":42}""";
        (Block block, BlockExecutionContext context) = SetupBlock(
            "status", "completed", completionUrlTemplate: COMPLETION_URL);
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("pending"), Status("completed"), RESULT_BODY]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(3, strategy.CallCount);
        Assert.Equal(1, strategy.DelayCount);
        Assert.Equal([STATUS_URL, STATUS_URL, COMPLETION_URL], strategy.RequestedUrls);
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RESULT_BODY, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task PollThenAction_ImmediateSuccess_FetchesCompletionUrlAsync()
    {
        const string RESULT_BODY = """{"answer":42}""";
        (Block block, BlockExecutionContext context) = SetupBlock(
            "status", "completed", completionUrlTemplate: COMPLETION_URL);
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("completed"), RESULT_BODY]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(2, strategy.CallCount);
        Assert.Equal(0, strategy.DelayCount);
        Assert.Equal([STATUS_URL, COMPLETION_URL], strategy.RequestedUrls);
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RESULT_BODY, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task PollThenAction_ParallelAiResultUrl_ParsesFactsFromActionResponseAsync()
    {
        // The poll URL is NOT a parallel.ai host; the completion URL is. Facts being
        // populated proves parser selection keys on the action URL.
        (Block block, BlockExecutionContext context) = SetupBlock(
            "status", "completed", completionUrlTemplate: PARALLEL_AI_RESULT_URL);
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("completed"), PARALLEL_AI_RESULT_BODY]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(PARALLEL_AI_RESULT_BODY, block.BlockResponse.JsonResponse);
    }

    [Fact]
    public async Task PollThenAction_TransientActionError_RetriedAndSucceedsAsync()
    {
        const string RESULT_BODY = """{"answer":42}""";
        (Block block, BlockExecutionContext context) = SetupBlock(
            "status", "completed", completionUrlTemplate: COMPLETION_URL);
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("completed"), HttpError(HttpStatusCode.ServiceUnavailable), RESULT_BODY]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(3, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RESULT_BODY, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task PollThenAction_HardActionFailure_FailsBlockAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "status", "completed", completionUrlTemplate: COMPLETION_URL);
        var strategy = new PollingStubStrategy(
            new TestTimeProvider(),
            [Status("completed"), HttpError(HttpStatusCode.Unauthorized)]);

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("401", ex.Message);
        Assert.Equal(COMPLETION_URL, strategy.RequestedUrls[^1]);
    }

    [Fact]
    public async Task ActionUrlWithoutPolling_FailsFastWithoutAnyHttpCallAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            jsonPath: null, successValues: null, completionUrlTemplate: COMPLETION_URL);
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("completed")]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(0, strategy.CallCount);
    }

    [Fact]
    public async Task InvalidActionUrl_FailsFastWithoutAnyHttpCallAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "status", "completed", completionUrlTemplate: "not-a-valid-url");
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("completed")]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(0, strategy.CallCount);
    }

    [Fact]
    public async Task PollFailureValue_DoesNotFetchCompletionUrlAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "status", "completed", failureValues: "failed", completionUrlTemplate: COMPLETION_URL);
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("failed")]);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.DoesNotContain(COMPLETION_URL, strategy.RequestedUrls);
    }

    [Fact]
    public void CompletionUrlTemplate_ResolvesPlaceholdersIntoExecutionState()
    {
        var workflow = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.WebApiCall, workflow)
        {
            Name = "Polling Call",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = "https://api.example.com/v1/tasks/runs/{run-id}",
                HttpMethod = "GET",
                PollUntilJsonPath = "status",
                PollUntilSuccessValues = "completed",
                PollCompletionUrlTemplate = "https://api.example.com/v1/tasks/runs/{run-id}/result",
            },
        };

        block.SetExecutionState(true, new Dictionary<string, string?> { ["run-id"] = "abc123" });

        var props = (WebApiCallProperties)block.Properties!;
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("https://api.example.com/v1/tasks/runs/abc123/result", props.ExecutionState.PollCompletionUrl);
    }

    // ---- Backward compatibility -------------------------------------------

    [Fact]
    public async Task NoPollPath_BehavesAsSingleRequestAsync()
    {
        const string RAW = """{"foo":"bar"}""";
        (Block block, BlockExecutionContext context) = SetupBlock(jsonPath: null, successValues: null);
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [RAW]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(1, strategy.CallCount);
        Assert.Equal(0, strategy.DelayCount);
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RAW, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task PollingEnabled_IgnoresDelayAfterRequestSecondsAsync()
    {
        // Polling supersedes the fixed delay: it should complete on the first
        // poll without ever invoking the fixed-delay path.
        (Block block, BlockExecutionContext context) =
            SetupBlock("status", "completed", delayAfterRequestSeconds: 9999);
        var strategy = new PollingStubStrategy(new TestTimeProvider(), [Status("completed")]);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(1, strategy.CallCount);
        Assert.NotNull(block.BlockResponse);
    }
}
