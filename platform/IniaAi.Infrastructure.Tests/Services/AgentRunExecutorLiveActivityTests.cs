using System.Text;
using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunExecutorLiveActivityTests
{
    private const string EXECUTION_ID = "wfexec1_block1_w_1";
    private const string TRACE_PACK_PATH = "trace/trace.ndjson";

    private static readonly AgentRunActivityRegistration _registration = AgentRunActivityRegistration.ForBlock(
        EXECUTION_ID, "org-test", "wfexec1", "block1", "Agent Block");

    private readonly StubAgentRuntimeClient _stub = new();
    private readonly RecordingFeed _feed = new();

    private sealed class RecordingFeed : IAgentRunActivityFeed
    {
        public List<string> Events { get; } = [];
        public List<AgentActivityDigest> Published { get; } = [];

        public void BeginRun(AgentRunActivityRegistration registration)
        {
            Events.Add($"begin:{registration.RunExecutionId}");
        }

        public void PublishDigest(string runExecutionId, AgentActivityDigest digest)
        {
            Events.Add($"publish:{runExecutionId}");
            Published.Add(digest);
        }

        public void ConcludeRun(string runExecutionId)
        {
            Events.Add($"conclude:{runExecutionId}");
        }

        public IReadOnlyList<AgentRunActivityEntry> SnapshotForExecution(
            string organizationId,
            string workflowExecutionId)
        {
            return [];
        }

        public IReadOnlyList<AgentRunActivityEntry> SnapshotForConversation(
            string organizationId,
            string conversationId)
        {
            return [];
        }

        public event EventHandler<AgentRunActivityEntry>? EntryChanged
        {
            add
            {
            }

            remove
            {
            }
        }
    }

    private AgentRunExecutor CreateExecutor()
    {
        return new AgentRunExecutor(
            _stub,
            new AgentRunPollGate(),
            NullLogger<AgentRunExecutor>.Instance,
            new SucceedingCollector(),
            timeProvider: null,
            activityFeed: _feed);
    }

    private sealed class SucceedingCollector : IAgentRunTraceCollector
    {
        public Task<AgentRunTraceCollectionResult> CollectAsync(
            string executionId,
            int attempt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentRunTraceCollectionResult(true, [], []));
        }
    }

    private static AgentRunMonitor Monitor(AgentRunStatus status, AgentRunResult? result = null)
    {
        return new AgentRunMonitor
        {
            ExecutionId = EXECUTION_ID,
            Status = status,
            Attempt = 1,
            PollInterval = TimeSpan.Zero,
            Result = result,
        };
    }

    private static byte[] MidRunTraceBytes()
    {
        string[] lines =
        [
            """{"uuid":"s1","at":"2026-08-13T13:00:00Z","type":"stage","event":"run.started","executionId":"e","attempt":1,"detail":{}}""",
            """{"uuid":"t1","at":"2026-08-13T13:00:10Z","type":"tool_call","toolUseId":"t1","toolName":"Bash","inputTruncated":"{\"command\":\"pdftotext -layout docs/Sample.pdf text.txt\"}","outputBytes":10,"durationMs":20,"errorClass":null}""",
            """{"uuid":"n1","at":"2026-08-13T13:00:12Z","type":"message","seq":1,"message":{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"I'll begin by reading the manifest."}]}}}""",
        ];
        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }

    [Fact]
    public async Task ARunWithARegistration_IsAnnouncedPublishedAndConcluded()
    {
        int polls = 0;
        _stub.OnStartRun = _ => Monitor(AgentRunStatus.Running);
        _stub.OnGetRun = _ => ++polls < 2
            ? Monitor(AgentRunStatus.Running)
            : Monitor(AgentRunStatus.Succeeded, new AgentRunResult("done", null));
        _stub.OnCompleteRun = _ => Monitor(AgentRunStatus.Succeeded);
        _stub.OnGetRunFile = (_, path) =>
        {
            Assert.Equal(TRACE_PACK_PATH, path);
            return MidRunTraceBytes();
        };

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor().ExecuteAsync(
            new AgentRunStartRequest(EXECUTION_ID, "work", RunToken: null),
            new AgentRunExecutionOptions(TimeSpan.FromMinutes(5), _registration),
            TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.Equal($"begin:{EXECUTION_ID}", _feed.Events[0]);
        Assert.Contains($"publish:{EXECUTION_ID}", _feed.Events);
        Assert.Equal($"conclude:{EXECUTION_ID}", _feed.Events[^1]);
        Assert.True(_feed.Published.Count >= 2);
        Assert.All(_feed.Published, static digest => Assert.NotEmpty(digest.Beats));
    }

    /// <summary>
    /// A chat consumer asks for its own pace and names its own steps: the feed refreshes many
    /// times between two status polls, every line carries the consumer's words, and the
    /// narration tidier is never asked about a line the consumer named.
    /// </summary>
    [Fact]
    public async Task AConsumersOwnPaceAndLabeller_RefreshTheFeedBetweenPolls_WithoutTheTidier()
    {
        int polls = 0;
        _stub.OnStartRun = _ => Monitor(AgentRunStatus.Running);
        _stub.OnGetRun = _ => ++polls < 3
            ? Monitor(AgentRunStatus.Running) with { PollInterval = TimeSpan.FromMilliseconds(400) }
            : Monitor(AgentRunStatus.Succeeded, new AgentRunResult("done", null));
        _stub.OnCompleteRun = _ => Monitor(AgentRunStatus.Succeeded);
        _stub.OnGetRunFile = (_, _) => MidRunTraceBytes();
        var tidier = new RecordingTidier();
        var executor = new AgentRunExecutor(
            _stub, new AgentRunPollGate(), NullLogger<AgentRunExecutor>.Instance, new SucceedingCollector(),
            timeProvider: null, activityFeed: _feed, narrationTidier: tidier);

        _ = await executor.ExecuteAsync(
            new AgentRunStartRequest(EXECUTION_ID, "work", RunToken: null),
            new AgentRunExecutionOptions(TimeSpan.FromMinutes(5), _registration)
            {
                LiveActivity = new AgentRunLiveActivityOptions
                {
                    PublishInterval = TimeSpan.FromMilliseconds(20),
                    Projection = new AgentActivityProjectionOptions { StepLabeller = new NamingLabeller() },
                },
            },
            TestContext.Current.CancellationToken);

        Assert.True(_feed.Published.Count > polls * 3, $"{_feed.Published.Count} publishes over {polls} polls");
        Assert.All(_feed.Published, static digest => Assert.Equal(["Converted a file", "Said: I'll begin by reading the manifest."], digest.Beats.Select(static beat => beat.Label)));
        Assert.Empty(tidier.Lines);
        Assert.Equal($"conclude:{EXECUTION_ID}", _feed.Events[^1]);
    }

    private sealed class NamingLabeller : IAgentActivityStepLabeller
    {
        public AgentActivityStep? LabelToolCall(AgentActivityToolCall call)
        {
            return new AgentActivityStep("Converted a file", "convert");
        }

        public AgentActivityStep? LabelRemark(string text)
        {
            return new AgentActivityStep("Said: " + text);
        }
    }

    private sealed class RecordingTidier : IAgentNarrationTidier
    {
        public List<AgentNarrationLine> Lines { get; } = [];

        public Task<IReadOnlyDictionary<string, string?>> TidyAsync(
            IReadOnlyList<AgentNarrationLine> lines,
            CancellationToken cancellationToken = default)
        {
            lock (Lines)
            {
                Lines.AddRange(lines);
            }

            return Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>());
        }
    }

    [Fact]
    public async Task ARunThatSettlesOnTheFirstPoll_StillPublishesItsFinalDigest()
    {
        _stub.OnStartRun = _ => Monitor(AgentRunStatus.Running);
        _stub.OnGetRun = _ => Monitor(AgentRunStatus.Succeeded, new AgentRunResult("done", null));
        _stub.OnCompleteRun = _ => Monitor(AgentRunStatus.Succeeded);
        _stub.OnGetRunFile = (_, _) => MidRunTraceBytes();

        _ = await CreateExecutor().ExecuteAsync(
            new AgentRunStartRequest(EXECUTION_ID, "work", RunToken: null),
            new AgentRunExecutionOptions(TimeSpan.FromMinutes(5), _registration),
            TestContext.Current.CancellationToken);

        Assert.Contains($"publish:{EXECUTION_ID}", _feed.Events);
        int publishIndex = _feed.Events.LastIndexOf($"publish:{EXECUTION_ID}");
        int concludeIndex = _feed.Events.IndexOf($"conclude:{EXECUTION_ID}");
        Assert.True(publishIndex < concludeIndex);
    }

    [Fact]
    public async Task AFailingTraceFetch_NeverDisturbsTheRun()
    {
        _stub.OnStartRun = _ => Monitor(AgentRunStatus.Running);
        _stub.OnGetRun = _ => Monitor(AgentRunStatus.Succeeded, new AgentRunResult("done", null));
        _stub.OnCompleteRun = _ => Monitor(AgentRunStatus.Succeeded);
        _stub.OnGetRunFile = (_, _) => Error.NotFound("AgentRun.NotFound", "no trace yet");

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor().ExecuteAsync(
            new AgentRunStartRequest(EXECUTION_ID, "work", RunToken: null),
            new AgentRunExecutionOptions(TimeSpan.FromMinutes(5), _registration),
            TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.DoesNotContain($"publish:{EXECUTION_ID}", _feed.Events);
        Assert.Equal($"conclude:{EXECUTION_ID}", _feed.Events[^1]);
    }

    [Fact]
    public async Task WithoutARegistration_TheFeedIsNeverTouched()
    {
        _stub.OnStartRun = _ => Monitor(AgentRunStatus.Running);
        _stub.OnGetRun = _ => Monitor(AgentRunStatus.Succeeded, new AgentRunResult("done", null));
        _stub.OnCompleteRun = _ => Monitor(AgentRunStatus.Succeeded);

        _ = await CreateExecutor().ExecuteAsync(
            new AgentRunStartRequest(EXECUTION_ID, "work", RunToken: null),
            new AgentRunExecutionOptions(TimeSpan.FromMinutes(5)),
            TestContext.Current.CancellationToken);

        Assert.Empty(_feed.Events);
    }

    [Fact]
    public async Task ACancelledCallerToken_CancelsTheLiveTraceFetchPromptly()
    {
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callerCancellation = new CancellationTokenSource();

        _stub.OnStartRun = _ => Monitor(AgentRunStatus.Running);
        _stub.OnGetRun = _ => Monitor(AgentRunStatus.Running) with { PollInterval = TimeSpan.FromMilliseconds(20) };
        _stub.OnGetRunFileAsync = async (_, _, fetchToken) =>
        {
            fetchStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, fetchToken);
            }
            catch (OperationCanceledException)
            {
                fetchCancelled.TrySetResult();
                throw;
            }

            return MidRunTraceBytes();
        };

        Task<ErrorOr<AgentRunConclusion>> executing = CreateExecutor().ExecuteAsync(
            new AgentRunStartRequest(EXECUTION_ID, "work", RunToken: null),
            new AgentRunExecutionOptions(TimeSpan.FromMinutes(5), _registration),
            callerCancellation.Token);

        await fetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        callerCancellation.Cancel();

        await fetchCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executing);
        Assert.Equal($"conclude:{EXECUTION_ID}", _feed.Events[^1]);
    }

    [Fact]
    public async Task AStartRefusal_ConcludesTheAnnouncedEntry()
    {
        _stub.OnStartRun = _ => Error.Failure("AgentRun.Capacity", "no slots");

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor().ExecuteAsync(
            new AgentRunStartRequest(EXECUTION_ID, "work", RunToken: null),
            new AgentRunExecutionOptions(TimeSpan.FromMinutes(5), _registration),
            TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(
            [$"begin:{EXECUTION_ID}", $"conclude:{EXECUTION_ID}"],
            _feed.Events);
    }
}
