using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace IniaAi.Infrastructure.Tests.Services;

public class AgentRunExecutorTests
{
    private const string EXECUTION_ID = "wfexec1_block1_w_1";

    private static readonly AgentRunExecutionOptions _patientOptions = new(TimeSpan.FromMinutes(5));
    private static readonly TimeSpan _instantPoll = TimeSpan.FromMilliseconds(1);
    private static readonly AgentRunTraceAnchor _anchor = new("org-1", "block-1");
    private static readonly TimeSpan _outageWait = TimeSpan.FromMinutes(10);

    private readonly StubAgentRuntimeClient _stub = new();
    private readonly RecordingCollector _collector = new();
    private readonly List<string> _events = [];

    private AgentRunExecutor CreateExecutor(
        IAgentRunTraceCollector? collector,
        AgentRunPollGate? pollGate = null,
        IAgentRunTracePersister? persister = null,
        ILogger<AgentRunExecutor>? logger = null,
        IAgentRunSessionBundleCustodian? custodian = null)
    {
        return new AgentRunExecutor(
            _stub,
            pollGate ?? new AgentRunPollGate(),
            logger ?? NullLogger<AgentRunExecutor>.Instance,
            collector,
            persister,
            bundleCustodian: custodian);
    }

    private sealed class RecordingLogger : ILogger<AgentRunExecutor>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private AgentRunExecutionOptions AnchoredOptions(AgentRunExecutionOptions? baseline = null)
    {
        return (baseline ?? _patientOptions) with { TraceAnchor = _anchor };
    }

    private void ArrangeSettlingRun(
        AgentRunStatus settledStatus,
        AgentRunResult? result = null,
        AgentRunError? error = null,
        bool recordEvents = false)
    {
        _stub.OnStartRun = request =>
        {
            if (recordEvents)
            {
                _events.Add("start");
            }

            return Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        };
        _stub.OnGetRun = executionId =>
        {
            if (recordEvents)
            {
                _events.Add("poll");
            }

            return Monitor(executionId, settledStatus, pollInterval: null, result, error);
        };
        _stub.OnCompleteRun = executionId =>
        {
            _events.Add("complete");
            return Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);
        };
    }

    private static AgentRunMonitor Monitor(
        string executionId,
        AgentRunStatus status,
        TimeSpan? pollInterval,
        AgentRunResult? result = null,
        AgentRunError? error = null)
    {
        return new AgentRunMonitor
        {
            ExecutionId = executionId,
            Status = status,
            Attempt = 1,
            PollInterval = pollInterval,
            Result = result,
            Error = error,
        };
    }

    private static AgentRunStartRequest Request(string executionId = EXECUTION_ID)
    {
        return new AgentRunStartRequest(executionId, "do the work", RunToken: null);
    }

    private static AgentRunExecutionOptions PatientOptions(
        int maxPasses = 1,
        int pollErrorTolerance = 0,
        TimeSpan? admissionPatience = null,
        TimeSpan? deadline = null)
    {
        return new AgentRunExecutionOptions(deadline ?? TimeSpan.FromMinutes(5))
        {
            MaxPasses = maxPasses,
            PollErrorTolerance = pollErrorTolerance,
            AdmissionPatience = admissionPatience ?? TimeSpan.Zero,
            ExecutionIdForPass = pass => $"wfexec1_block1_w_{pass}",
        };
    }

    private static Error CapacityRefusal(TimeSpan retryAfter, string code = AgentRunAdmissionErrors.CAPACITY)
    {
        return Error.Failure(
            code,
            "Agent runtime refused the run for capacity",
            new Dictionary<string, object>
            {
                [AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY] = retryAfter,
            });
    }

    private sealed class RecordingCollector : IAgentRunTraceCollector
    {
        public List<(string ExecutionId, int Attempt)> Collections { get; } = [];
        public Func<string, AgentRunTraceCollectionResult>? OnCollect { get; set; }
        public List<string>? Events { get; set; }

        public Task<AgentRunTraceCollectionResult> CollectAsync(
            string executionId,
            int attempt,
            CancellationToken cancellationToken = default)
        {
            Collections.Add((executionId, attempt));
            Events?.Add("collect");

            return Task.FromResult(
                OnCollect?.Invoke(executionId)
                ?? new AgentRunTraceCollectionResult(true, [], []));
        }
    }

    private sealed class RecordingCustodian : IAgentRunSessionBundleCustodian
    {
        public List<(AgentRunSessionBundleAnchor Anchor, string ExecutionId, int Attempt, int DroppedOutFileCount)> Taken { get; } = [];
        public bool Result { get; set; } = true;
        public bool Throw { get; set; }
        public List<string>? Events { get; set; }

        public Task<bool> TakeCustodyAsync(
            AgentRunSessionBundleAnchor anchor,
            string executionId,
            int attempt,
            int droppedOutFileCount = 0,
            CancellationToken cancellationToken = default)
        {
            Taken.Add((anchor, executionId, attempt, droppedOutFileCount));
            Events?.Add("custody");

            return Throw
                ? throw new InvalidOperationException("Custodian contract violation")
                : Task.FromResult(Result);
        }

        public Task<AgentRunSessionBundle?> ResolveNewestBeforeAsync(
            string organizationId,
            string blockId,
            int dispatchRound,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AgentRunSessionBundle?>(null);
        }

        public Task<AgentRunSessionBundle?> ResolveNewestForConversationAsync(
            string organizationId,
            string conversationId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AgentRunSessionBundle?>(null);
        }

        public Task<byte[]?> ReadBytesAsync(
            AgentRunSessionBundle held,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<byte[]?>(null);
        }
    }

    private sealed class RecordingPersister : IAgentRunTracePersister
    {
        public List<IReadOnlyList<AgentRunTraceFile>> Persisted { get; } = [];
        public bool Result { get; set; } = true;
        public bool Throw { get; set; }
        public List<string>? Events { get; set; }

        public Task<bool> PersistAsync(
            IReadOnlyList<AgentRunTraceFile> files,
            CancellationToken cancellationToken = default)
        {
            Events?.Add("persist");
            Persisted.Add(files);
            if (Throw)
            {
                throw new InvalidOperationException("Persister contract violation");
            }

            return Task.FromResult(Result);
        }
    }

    private static AgentRunTraceCollectionResult CollectedRows(params string[] paths)
    {
        return new AgentRunTraceCollectionResult(
            true,
            [.. paths.Select(static path => new AgentRunTraceFile { Path = path })],
            []);
    }

    private sealed class ThrowingCollector : IAgentRunTraceCollector
    {
        public Task<AgentRunTraceCollectionResult> CollectAsync(
            string executionId,
            int attempt,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Collector contract violation");
        }
    }

    [Fact]
    public async Task ExecuteAsync_SettledRun_CollectsBeforeAcknowledging()
    {
        ArrangeSettlingRun(AgentRunStatus.Succeeded, result: new AgentRunResult("agent output", null));
        _collector.Events = _events;
        var traceFile = new AgentRunTraceFile { Path = "trace/trace.ndjson", Content = [1, 2, 3] };
        _collector.OnCollect = _ => new AgentRunTraceCollectionResult(true, [traceFile], []);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.Monitor.TryGetResult(out AgentRunResult? result));
        Assert.Equal("agent output", result.Output);
        Assert.False(conclusion.Value.CompleteWithheld);
        Assert.False(Assert.Single(conclusion.Value.Attempts).Unacknowledged);
        AgentRunTraceFile returned = Assert.Single(conclusion.Value.TraceFiles ?? []);
        Assert.Same(traceFile, returned);
        Assert.Equal([(EXECUTION_ID, 1)], _collector.Collections);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
        Assert.Equal(["collect", "complete"], _events);
    }

    [Fact]
    public async Task ExecuteAsync_RejectedComplete_MarksItsAttemptUnacknowledged()
    {
        ArrangeSettlingRun(AgentRunStatus.Succeeded, result: new AgentRunResult("agent output", null));
        _stub.OnCompleteRun = _ => Error.Conflict("AgentRun.NotCompletable", "still running");

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.CompleteWithheld);
        Assert.True(Assert.Single(conclusion.Value.Attempts).Unacknowledged);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowingCompleteCall_MarksItsAttemptUnacknowledged()
    {
        ArrangeSettlingRun(AgentRunStatus.Succeeded, result: new AgentRunResult("agent output", null));
        _stub.OnCompleteRun = _ => throw new HttpRequestException("socket closed");

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.CompleteWithheld);
        Assert.True(Assert.Single(conclusion.Value.Attempts).Unacknowledged);
    }

    [Fact]
    public async Task ExecuteAsync_RunSettledWithFailure_IsAConclusionNotAnError()
    {
        ArrangeSettlingRun(AgentRunStatus.Failed, error: new AgentRunError("errored_result"));

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.Monitor.TryGetError(out AgentRunError? error));
        Assert.Equal("errored_result", error.Subtype);
        Assert.False(conclusion.Value.CompleteWithheld);
        _ = Assert.Single(_collector.Collections);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownStatus_ErrorsCollectsForEvidenceAndStillWithholds()
    {
        ArrangeSettlingRun(AgentRunStatus.Unknown);
        _stub.OnGetRun = executionId => Monitor(executionId, AgentRunStatus.Unknown, _instantPoll);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.UNKNOWN_STATUS, conclusion.FirstError.Code);
        Assert.True(Assert.Single(AgentRunAttempts.Of(conclusion.FirstError)).Unacknowledged);
        _ = Assert.Single(_collector.Collections);
        Assert.Empty(_stub.CompletedExecutionIds);
        Assert.Equal([EXECUTION_ID], _stub.CancelledExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_FailedCollection_WithholdsCompleteButReturnsPartialRows()
    {
        ArrangeSettlingRun(AgentRunStatus.Succeeded, result: new AgentRunResult("agent output", null));
        var partial = new AgentRunTraceFile { Path = "trace/trace.ndjson", Content = [7] };
        _collector.OnCollect = _ => new AgentRunTraceCollectionResult(false, [partial], ["fetch failed"]);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.CompleteWithheld);
        _ = Assert.Single(conclusion.Value.TraceFiles ?? []);
        Assert.True(Assert.Single(conclusion.Value.Attempts).Unacknowledged);
        Assert.Empty(_stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_MissingCollector_WithholdsCompleteWithNullRows()
    {
        ArrangeSettlingRun(AgentRunStatus.Succeeded, result: new AgentRunResult("agent output", null));

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(collector: null).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.CompleteWithheld);
        Assert.Null(conclusion.Value.TraceFiles);
        Assert.Empty(_stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowingCollector_WithholdsCompleteWithNullRows()
    {
        ArrangeSettlingRun(AgentRunStatus.Succeeded, result: new AgentRunResult("agent output", null));

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(new ThrowingCollector()).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.CompleteWithheld);
        Assert.Null(conclusion.Value.TraceFiles);
        Assert.Empty(_stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_StartRejection_ErrorsWithoutAcknowledging()
    {
        _stub.OnStartRun = _ => Error.Conflict("AgentRun.PayloadConflict", "same id, different payload");

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.START_FAILED, conclusion.FirstError.Code);
        Assert.Contains("AgentRun.PayloadConflict", conclusion.FirstError.Description);
        Assert.Empty(_stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_ChatDisabled_IsNeverWaitedOut_AndKeepsItsCode()
    {
        _stub.OnStartRun = _ => Error.Failure(AgentRunAdmissionErrors.CHAT_DISABLED, "chat admission is off");

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunAdmissionErrors.CHAT_DISABLED, conclusion.FirstError.Code);
        Assert.Single(_stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteAsync_AdmissionRefusal_ReachesTheCallerWithItsCodeAndDelayIntact()
    {
        var advertised = TimeSpan.FromSeconds(30);
        _stub.OnStartRun = _ => Error.Failure(
            AgentRunAdmissionErrors.CAPACITY,
            "Agent runtime refused run 'x' for capacity; retry after 30s",
            new Dictionary<string, object>
            {
                [AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY] = advertised,
            });

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        // A re-dispatcher decides on the code and the delay, so re-coding this to START_FAILED
        // would leave it substring-matching prose to tell a safe re-post from a conflict.
        Assert.Equal(AgentRunAdmissionErrors.CAPACITY, conclusion.FirstError.Code);
        Assert.Equal(
            advertised,
            conclusion.FirstError.Metadata![AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY]);
        Assert.Empty(_stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_PollError_ErrorsCollectsTheEvidenceAndAcknowledges()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = _ => Error.Failure("AgentRun.Unreachable", "socket closed");
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.POLL_FAILED, conclusion.FirstError.Code);
        Assert.Contains("AgentRun.Unreachable", conclusion.FirstError.Description);
        _ = Assert.Single(_collector.Collections);
        Assert.Equal([EXECUTION_ID], _stub.CancelledExecutionIds);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_DeadlineExceeded_ErrorsWithLastStatusAndStillAcknowledges()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => Monitor(executionId, AgentRunStatus.Running, TimeSpan.FromMilliseconds(40));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), new AgentRunExecutionOptions(TimeSpan.FromMilliseconds(120)), TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.DEADLINE_EXCEEDED, conclusion.FirstError.Code);
        Assert.NotNull(conclusion.FirstError.Metadata);
        Assert.Equal(
            AgentRunStatus.Running,
            conclusion.FirstError.Metadata[AgentRunExecutorErrors.STATUS_METADATA_KEY]);
        Assert.Equal([EXECUTION_ID], _stub.CancelledExecutionIds);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_AwaitingInput_ErrorsCollectsTheEvidenceAndAcknowledges()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => Monitor(executionId, AgentRunStatus.AwaitingInput, _instantPoll);
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.AWAITING_INPUT, conclusion.FirstError.Code);
        _ = Assert.Single(_collector.Collections);
        Assert.Equal([EXECUTION_ID], _stub.CancelledExecutionIds);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledCaller_TearsDownThroughSettleCollectPersistAcknowledge()
    {
        using var cancellation = new CancellationTokenSource();
        var persister = new RecordingPersister { Events = _events };
        _collector.Events = _events;
        bool cancelled = false;
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId =>
        {
            if (cancelled)
            {
                _events.Add("settle-poll");
                return Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null) with
                {
                    EstimatedCostUsd = 0.42m,
                };
            }

            cancellation.Cancel();
            return Monitor(executionId, AgentRunStatus.Running, TimeSpan.FromMilliseconds(1));
        };
        _stub.OnCancelRun = executionId =>
        {
            cancelled = true;
            _events.Add("cancel");
            return Monitor(executionId, AgentRunStatus.Cancelling, _instantPoll);
        };
        _stub.OnCompleteRun = executionId =>
        {
            _events.Add("complete");
            return Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
        };
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        var observed = new List<AgentRunAttempt>();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateExecutor(_collector, persister: persister).ExecuteAsync(
                Request(),
                AnchoredOptions() with { OnAttempt = observed.Add },
                cancellation.Token));

        Assert.Equal(["cancel", "settle-poll", "collect", "persist", "complete"], _events);
        AgentRunAttempt attempt = Assert.Single(observed);
        Assert.Equal(AgentRunStatus.Cancelled, attempt.Status);
        Assert.Equal(0.42m, attempt.EstimatedCostUsd);
        Assert.False(attempt.Unacknowledged);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledCallerWithFailingPersist_WithholdsAndLeavesThePackCollectable()
    {
        using var cancellation = new CancellationTokenSource();
        var persister = new RecordingPersister { Result = false };
        bool cancelled = false;
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId =>
        {
            if (cancelled)
            {
                return Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
            }

            cancellation.Cancel();
            return Monitor(executionId, AgentRunStatus.Running, TimeSpan.FromMilliseconds(1));
        };
        _stub.OnCancelRun = executionId =>
        {
            cancelled = true;
            return Monitor(executionId, AgentRunStatus.Cancelling, _instantPoll);
        };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        var observed = new List<AgentRunAttempt>();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateExecutor(_collector, persister: persister).ExecuteAsync(
                Request(),
                AnchoredOptions() with { OnAttempt = observed.Add },
                cancellation.Token));

        Assert.Equal([EXECUTION_ID], _stub.CancelledExecutionIds);
        Assert.Empty(_stub.CompletedExecutionIds);
        Assert.True(Assert.Single(observed).Unacknowledged);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledCaller_HandsTheBoughtRunsToOnAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        var observed = new List<AgentRunAttempt>();
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId =>
        {
            cancellation.Cancel();
            return Monitor(executionId, AgentRunStatus.Running, TimeSpan.FromMilliseconds(1));
        };
        _stub.OnCompleteRun = executionId => Error.Failure(
            "AgentRun.NotCompletable", "still cancelling");

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateExecutor(_collector).ExecuteAsync(
                Request(),
                _patientOptions with { OnAttempt = observed.Add },
                cancellation.Token));

        AgentRunAttempt attempt = Assert.Single(observed);
        Assert.Equal(1, attempt.Pass);
        Assert.Equal(EXECUTION_ID, attempt.ExecutionId);
        Assert.True(attempt.Unacknowledged);
    }

    [Fact]
    public async Task ExecuteAsync_MissingRetryAfter_FallsBackToJitteredPollingInsteadOfFailing()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, pollInterval: null);
        _stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.Monitor.TryGetResult(out _));
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentRuns_NeverExceedTheGateCap()
    {
        const int runCount = 16;
        _ = ThreadPool.SetMinThreads(runCount * 2, runCount * 2);

        int inFlight = 0;
        int maxInFlight = 0;
        var pollsByRun = new ConcurrentDictionary<string, int>();
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId =>
        {
            int current = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, current);
            // Waits on purpose: holds the slot long enough for parallel calls to overlap.
            Thread.Sleep(20);
            _ = Interlocked.Decrement(ref inFlight);

            return pollsByRun.AddOrUpdate(executionId, 1, static (_, polls) => polls + 1) < 2
                ? Monitor(executionId, AgentRunStatus.Running, _instantPoll)
                : Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null, new AgentRunResult("done", null));
        };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        AgentRunExecutor executor = CreateExecutor(_collector);
        ErrorOr<AgentRunConclusion>[] conclusions = await Task.WhenAll(Enumerable.Range(0, runCount)
            .Select(run => executor.ExecuteAsync(
                Request($"wfexec1_block{run}_w_1"), _patientOptions, TestContext.Current.CancellationToken)));

        Assert.All(conclusions, static conclusion => Assert.False(conclusion.IsError));
        Assert.InRange(maxInFlight, 1, AgentRunPollGate.MAX_CONCURRENT_POLLS);
    }

    private static void InterlockedMax(ref int location, int candidate)
    {
        int observed = Volatile.Read(ref location);
        while (candidate > observed)
        {
            int previous = Interlocked.CompareExchange(ref location, candidate, observed);
            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }

    [Fact]
    public async Task ExecuteAsync_CapacityRefusal_WaitsTheAdvisedDelayAndStartsAgain()
    {
        var refusals = 0;
        _stub.OnStartRun = request =>
        {
            refusals++;
            return refusals <= 2
                ? CapacityRefusal(TimeSpan.FromMilliseconds(10))
                : Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        };
        _stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(),
            PatientOptions(admissionPatience: TimeSpan.FromSeconds(5)),
            TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError, conclusion.IsError ? conclusion.FirstError.Description : "");
        Assert.True(conclusion.Value.Monitor.TryGetResult(out _));
        Assert.Equal(3, _stub.StartRequests.Count);
        Assert.All(_stub.StartRequests, request => Assert.Equal(EXECUTION_ID, request.ExecutionId));
    }

    [Fact]
    public async Task ExecuteAsync_ShuttingDownRefusal_IsWaitedOutLikeCapacity()
    {
        var refusals = 0;
        _stub.OnStartRun = request =>
        {
            refusals++;
            return refusals == 1
                ? CapacityRefusal(TimeSpan.FromMilliseconds(10), AgentRunAdmissionErrors.SHUTTING_DOWN)
                : Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        };
        _stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(),
            PatientOptions(admissionPatience: TimeSpan.FromSeconds(5)),
            TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError, conclusion.IsError ? conclusion.FirstError.Description : "");
        Assert.Equal(2, _stub.StartRequests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_AdmissionWait_IsSpentOutsideTheRunDeadline()
    {
        var refusals = 0;
        _stub.OnStartRun = request =>
        {
            refusals++;
            return refusals <= 3
                ? CapacityRefusal(TimeSpan.FromMilliseconds(40))
                : Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        };
        _stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(),
            PatientOptions(admissionPatience: TimeSpan.FromSeconds(5), deadline: TimeSpan.FromMilliseconds(80)),
            TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError, conclusion.IsError ? conclusion.FirstError.Description : "");
        Assert.True(conclusion.Value.Monitor.TryGetResult(out _));
    }

    [Fact]
    public async Task ExecuteAsync_RefusalsOutlastingPatience_FailWithAdmissionExhausted()
    {
        _stub.OnStartRun = _ => CapacityRefusal(TimeSpan.FromMilliseconds(10));

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(),
            PatientOptions(admissionPatience: TimeSpan.FromMilliseconds(30)),
            TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.ADMISSION_EXHAUSTED, conclusion.FirstError.Code);
        Assert.InRange(_stub.StartRequests.Count, 2, 20);
        Assert.Empty(_stub.CompletedExecutionIds);
        Assert.Empty(_stub.CancelledExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_NoPatience_KeepsTheRefusalVerbatimForADurableRedispatcher()
    {
        _stub.OnStartRun = _ => CapacityRefusal(TimeSpan.FromSeconds(30));

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(), TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunAdmissionErrors.CAPACITY, conclusion.FirstError.Code);
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            conclusion.FirstError.Metadata![AgentRunAdmissionErrors.RETRY_AFTER_METADATA_KEY]);
        _ = Assert.Single(_stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteAsync_RetryableFailure_ReRegistersAsTheNextPassAndSucceeds()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => executionId.EndsWith("_1", StringComparison.Ordinal)
            ? Monitor(
                executionId, AgentRunStatus.Failed, pollInterval: null,
                error: new AgentRunError(AgentRunSubtypes.RUNTIME_SHUTDOWN))
            : Monitor(
                executionId, AgentRunStatus.Succeeded, pollInterval: null,
                result: new AgentRunResult("second pass output", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 3), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError, conclusion.IsError ? conclusion.FirstError.Description : "");
        Assert.True(conclusion.Value.Monitor.TryGetResult(out AgentRunResult? result));
        Assert.Equal("second pass output", result.Output);
        Assert.Equal(
            ["wfexec1_block1_w_1", "wfexec1_block1_w_2"],
            _stub.StartRequests.Select(static request => request.ExecutionId));
        Assert.Equal(["wfexec1_block1_w_1", "wfexec1_block1_w_2"], _stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_RetryableFailure_AcquiresEachPassTracePackBeforeItsTeardown()
    {
        _collector.Events = _events;
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => executionId.EndsWith("_1", StringComparison.Ordinal)
            ? Monitor(
                executionId, AgentRunStatus.Failed, pollInterval: null,
                error: new AgentRunError(AgentRunSubtypes.INTERNAL_ERROR))
            : Monitor(
                executionId, AgentRunStatus.Succeeded, pollInterval: null,
                result: new AgentRunResult("done", null));
        _stub.OnCompleteRun = executionId =>
        {
            _events.Add($"complete {executionId}");
            return Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);
        };
        _collector.OnCollect = executionId => new AgentRunTraceCollectionResult(
            true, [new AgentRunTraceFile { Path = $"trace/{executionId}.ndjson", Content = [1] }], []);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 2), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError, conclusion.IsError ? conclusion.FirstError.Description : "");
        Assert.Equal([("wfexec1_block1_w_1", 1), ("wfexec1_block1_w_2", 1)], _collector.Collections);
        Assert.Equal(
            ["collect", "complete wfexec1_block1_w_1", "collect", "complete wfexec1_block1_w_2"],
            _events);
        Assert.Equal(
            ["trace/wfexec1_block1_w_1.ndjson", "trace/wfexec1_block1_w_2.ndjson"],
            (conclusion.Value.TraceFiles ?? []).Select(static file => file.Path));
    }

    [Fact]
    public async Task ExecuteAsync_NonRetryableFailure_NeverBuysASecondRun()
    {
        ArrangeSettlingRun(AgentRunStatus.Failed, error: new AgentRunError(AgentRunSubtypes.BUDGET_EXHAUSTED));

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 3), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.Monitor.TryGetError(out AgentRunError? error));
        Assert.Equal(AgentRunSubtypes.BUDGET_EXHAUSTED, error.Subtype);
        _ = Assert.Single(_stub.StartRequests);
        Assert.Equal(1, Assert.Single(conclusion.Value.Attempts).Pass);
    }

    [Fact]
    public async Task ExecuteAsync_SpendCarryingRunChildError_NeverBuysASecondRun()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Failed, pollInterval: null,
            error: new AgentRunError(AgentRunSubtypes.RUN_CHILD_ERROR)) with
        { EstimatedCostUsd = 0.02m };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 3), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.Monitor.TryGetError(out AgentRunError? error));
        Assert.Equal(AgentRunSubtypes.RUN_CHILD_ERROR, error.Subtype);
        _ = Assert.Single(_stub.StartRequests);
        AgentRunAttempt attempt = Assert.Single(conclusion.Value.Attempts);
        Assert.Equal(1, attempt.Pass);
        Assert.Equal(0.02m, attempt.EstimatedCostUsd);
    }

    [Fact]
    public async Task ExecuteAsync_EveryPassRetryable_ConcludesWithTheWholeLedger()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Failed, pollInterval: null,
            error: new AgentRunError(AgentRunSubtypes.UID_POOL_EXHAUSTED)) with
        { EstimatedCostUsd = 0.02m };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 3), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.Equal(3, _stub.StartRequests.Count);
        Assert.Equal(3, conclusion.Value.Attempts.Count);
        Assert.Equal([1, 2, 3], conclusion.Value.Attempts.Select(static attempt => attempt.Pass));
        Assert.All(
            conclusion.Value.Attempts,
            attempt => Assert.Equal(AgentRunSubtypes.UID_POOL_EXHAUSTED, attempt.FailureSubtype));
        Assert.Equal(0.06m, conclusion.Value.Attempts.TotalCostUsd());
    }

    private sealed class InstantTimeProvider : TimeProvider
    {
        public ConcurrentQueue<TimeSpan> Delays { get; } = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Delays.Enqueue(dueTime);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new SpentTimer();
        }
    }

    private sealed class SpentTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return true;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private void ArrangeUpstreamErrorThen(
        Func<string, AgentRunMonitor> afterFirstPass,
        decimal firstPassCostUsd = 0.001m,
        decimal? budgetUsd = 1m)
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => executionId.EndsWith("_1", StringComparison.Ordinal)
            ? Monitor(
                executionId, AgentRunStatus.Failed, pollInterval: null,
                error: new AgentRunError(AgentRunSubtypes.UPSTREAM_ERROR)) with
            {
                EstimatedCostUsd = firstPassCostUsd,
                Fuses = budgetUsd is null ? null : new AgentRunFuses(budgetUsd),
            }
            : afterFirstPass(executionId);
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);
    }

    /// <summary>Fires every timer at once except the outage wait, which it never fires and instead cancels.</summary>
    private sealed class CancelDuringOutageWait(CancellationTokenSource cancellation) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == _outageWait)
            {
                cancellation.Cancel();
            }
            else
            {
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            }

            return new SpentTimer();
        }
    }

    private (AgentRunExecutor Executor, InstantTimeProvider Clock, RecordingLogger Log) OutageExecutor()
    {
        var clock = new InstantTimeProvider();
        var log = new RecordingLogger();
        var executor = new AgentRunExecutor(
            _stub, new AgentRunPollGate(), log, _collector, timeProvider: clock);
        return (executor, clock, log);
    }

    private static AgentRunExecutionOptions OutageOptions()
    {
        return PatientOptions(maxPasses: 3) with { UpstreamErrorRetryDelay = _outageWait };
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamError_WaitsThenBuysOneFreshPassThatCompletesTheBlock()
    {
        ArrangeUpstreamErrorThen(executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null,
            result: new AgentRunResult("second pass output", null)));
        (AgentRunExecutor executor, InstantTimeProvider clock, RecordingLogger log) = OutageExecutor();

        ErrorOr<AgentRunConclusion> conclusion = await executor.ExecuteAsync(
            Request(), OutageOptions(), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError, conclusion.IsError ? conclusion.FirstError.Description : "");
        Assert.True(conclusion.Value.Monitor.TryGetResult(out AgentRunResult? result));
        Assert.Equal("second pass output", result.Output);
        Assert.Equal(
            ["wfexec1_block1_w_1", "wfexec1_block1_w_2"],
            _stub.StartRequests.Select(static request => request.ExecutionId));
        Assert.Contains(_outageWait, clock.Delays);
        Assert.Contains(log.Messages, message =>
            message.Contains("upstream_error", StringComparison.Ordinal)
            && message.Contains("waiting 00:10:00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamErrorTwice_FailsWithBothPassesInTheLedger()
    {
        ArrangeUpstreamErrorThen(executionId => Monitor(
            executionId, AgentRunStatus.Failed, pollInterval: null,
            error: new AgentRunError(AgentRunSubtypes.UPSTREAM_ERROR)) with
        { EstimatedCostUsd = 0.001m, Fuses = new AgentRunFuses(1m) });
        (AgentRunExecutor executor, _, _) = OutageExecutor();

        ErrorOr<AgentRunConclusion> conclusion = await executor.ExecuteAsync(
            Request(), OutageOptions(), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.Monitor.TryGetError(out AgentRunError? error));
        Assert.Equal(AgentRunSubtypes.UPSTREAM_ERROR, error.Subtype);
        Assert.Equal(2, _stub.StartRequests.Count);
        Assert.Equal([1, 2], conclusion.Value.Attempts.Select(static attempt => attempt.Pass));
        Assert.All(
            conclusion.Value.Attempts,
            attempt => Assert.Equal(AgentRunSubtypes.UPSTREAM_ERROR, attempt.FailureSubtype));
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamErrorAfterMoreThanAQuarterOfTheBudget_BuysNoSecondPass()
    {
        ArrangeUpstreamErrorThen(
            executionId => throw new InvalidOperationException($"no second pass expected, got {executionId}"),
            firstPassCostUsd: 0.26m,
            budgetUsd: 1m);
        (AgentRunExecutor executor, InstantTimeProvider clock, _) = OutageExecutor();

        ErrorOr<AgentRunConclusion> conclusion = await executor.ExecuteAsync(
            Request(), OutageOptions(), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        _ = Assert.Single(_stub.StartRequests);
        Assert.Equal(0.26m, Assert.Single(conclusion.Value.Attempts).EstimatedCostUsd);
        Assert.DoesNotContain(_outageWait, clock.Delays);
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamErrorAtExactlyAQuarterOfTheBudget_BuysTheSecondPass()
    {
        ArrangeUpstreamErrorThen(
            executionId => Monitor(
                executionId, AgentRunStatus.Succeeded, pollInterval: null,
                result: new AgentRunResult("done", null)),
            firstPassCostUsd: 0.25m,
            budgetUsd: 1m);
        (AgentRunExecutor executor, _, _) = OutageExecutor();

        ErrorOr<AgentRunConclusion> conclusion = await executor.ExecuteAsync(
            Request(), OutageOptions(), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.Equal(2, _stub.StartRequests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamErrorFromARuntimeThatEchoesNoBudget_BuysNoSecondPass()
    {
        ArrangeUpstreamErrorThen(
            executionId => throw new InvalidOperationException($"no second pass expected, got {executionId}"),
            budgetUsd: null);
        (AgentRunExecutor executor, _, _) = OutageExecutor();

        ErrorOr<AgentRunConclusion> conclusion = await executor.ExecuteAsync(
            Request(), OutageOptions(), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        _ = Assert.Single(_stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringTheOutageWait_StopsWithTheFailedPassRecorded()
    {
        ArrangeUpstreamErrorThen(
            executionId => throw new InvalidOperationException($"no second pass expected, got {executionId}"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var executor = new AgentRunExecutor(
            _stub, new AgentRunPollGate(), NullLogger<AgentRunExecutor>.Instance, _collector,
            timeProvider: new CancelDuringOutageWait(cancellation));
        var recorded = new List<AgentRunAttempt>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            Request(), OutageOptions() with { OnAttempt = recorded.Add }, cancellation.Token));

        _ = Assert.Single(_stub.StartRequests);
        Assert.Equal(AgentRunSubtypes.UPSTREAM_ERROR, Assert.Single(recorded).FailureSubtype);
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamErrorWithASinglePassAllowed_BuysNoSecondPass()
    {
        ArrangeUpstreamErrorThen(
            executionId => throw new InvalidOperationException($"no second pass expected, got {executionId}"));
        (AgentRunExecutor executor, InstantTimeProvider clock, _) = OutageExecutor();

        ErrorOr<AgentRunConclusion> conclusion = await executor.ExecuteAsync(
            Request(), OutageOptions() with { MaxPasses = 1 }, TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        _ = Assert.Single(_stub.StartRequests);
        Assert.DoesNotContain(_outageWait, clock.Delays);
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamErrorWithoutARetryDelay_BuysNoSecondPass()
    {
        ArrangeUpstreamErrorThen(
            executionId => throw new InvalidOperationException($"no second pass expected, got {executionId}"));
        (AgentRunExecutor executor, _, _) = OutageExecutor();

        ErrorOr<AgentRunConclusion> conclusion = await executor.ExecuteAsync(
            Request(), PatientOptions(maxPasses: 3), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        _ = Assert.Single(_stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteAsync_MaxPassesOne_NeverRetriesEvenARetryableFailure()
    {
        ArrangeSettlingRun(AgentRunStatus.Failed, error: new AgentRunError(AgentRunSubtypes.RUNTIME_SHUTDOWN));

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        _ = Assert.Single(_stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteAsync_SucceededRun_ReportsTheCostTheRuntimeMeasured()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null)) with
        { EstimatedCostUsd = 0.1234m };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.Equal(0.1234m, conclusion.Value.Attempts.TotalCostUsd());
        AgentRunAttempt attempt = Assert.Single(conclusion.Value.Attempts);
        Assert.Equal(1, attempt.Pass);
        Assert.Equal(EXECUTION_ID, attempt.ExecutionId);
        Assert.Equal(AgentRunStatus.Succeeded, attempt.Status);
        Assert.Equal(0.1234m, attempt.EstimatedCostUsd);
        Assert.Null(attempt.FailureSubtype);
    }

    [Fact]
    public async Task ExecuteAsync_OneTransientPollError_DoesNotVoidALiveRun()
    {
        var polls = 0;
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId =>
        {
            polls++;
            return polls == 1
                ? Error.Failure("AgentRun.Unreachable", "socket closed")
                : Monitor(
                    executionId, AgentRunStatus.Succeeded, pollInterval: null,
                    result: new AgentRunResult("survived", null));
        };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(pollErrorTolerance: 3), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError, conclusion.IsError ? conclusion.FirstError.Description : "");
        Assert.True(conclusion.Value.Monitor.TryGetResult(out AgentRunResult? result));
        Assert.Equal("survived", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_PollErrorsPastTolerance_GiveUpOnTheRun()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = _ => Error.Failure("AgentRun.Unreachable", "socket closed");
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(pollErrorTolerance: 2), TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.POLL_FAILED, conclusion.FirstError.Code);
        Assert.Equal([EXECUTION_ID], _stub.CancelledExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_TolerancePardonsOnlyConsecutiveErrors()
    {
        var polls = 0;
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId =>
        {
            polls++;
            return polls is 2 or 5
                ? Monitor(executionId, AgentRunStatus.Running, _instantPoll)
                : Error.Failure("AgentRun.Unreachable", "socket closed");
        };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(pollErrorTolerance: 2), TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.POLL_FAILED, conclusion.FirstError.Code);

        // 8 polls in the loop (errors at 1, 3-4 and 6-8 with pardons after 2 and 5), then the
        // teardown's one settle probe.
        Assert.Equal(9, polls);
    }

    [Fact]
    public async Task ExecuteAsync_StartThatNeverLanded_LeavesTheLedgerEmpty()
    {
        _stub.OnStartRun = _ => CapacityRefusal(TimeSpan.FromMilliseconds(5));
        var observed = new List<AgentRunAttempt>();

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(),
            PatientOptions(admissionPatience: TimeSpan.FromMilliseconds(20)) with { OnAttempt = observed.Add },
            TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.ADMISSION_EXHAUSTED, conclusion.FirstError.Code);
        Assert.Empty(AgentRunAttempts.Of(conclusion.FirstError));
        Assert.Empty(observed);
    }

    [Fact]
    public async Task ExecuteAsync_RunSettlingInsideTheLastPollWindow_ConcludesInsteadOfExpiring()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, TimeSpan.FromMinutes(10));
        _stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(deadline: TimeSpan.FromMilliseconds(50)), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.Monitor.TryGetResult(out _));
    }

    [Fact]
    public async Task ExecuteAsync_DeadlineOnARunStillLive_HarvestsTheSettledCostIntoTheLedger()
    {
        var polls = 0;
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId =>
        {
            polls++;
            return polls <= 3
                ? Monitor(executionId, AgentRunStatus.Running, TimeSpan.FromMilliseconds(30))
                : Monitor(
                    executionId, AgentRunStatus.Failed, pollInterval: null,
                    error: new AgentRunError(AgentRunSubtypes.DEADLINE_EXCEEDED)) with
                { EstimatedCostUsd = 0.42m };
        };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(deadline: TimeSpan.FromMilliseconds(60)), TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.DEADLINE_EXCEEDED, conclusion.FirstError.Code);
        Assert.Equal([EXECUTION_ID], _stub.CancelledExecutionIds);
        AgentRunAttempt attempt = Assert.Single(AgentRunAttempts.Of(conclusion.FirstError));
        Assert.Equal(0.42m, attempt.EstimatedCostUsd);
        Assert.Equal(AgentRunStatus.Failed, attempt.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ShedThenStaleAbortedRegistration_BuysTheNextPassAFreshId()
    {
        var starts = 0;
        _stub.OnStartRun = request =>
        {
            starts++;
            return starts switch
            {
                1 => CapacityRefusal(TimeSpan.FromMilliseconds(5)),
                2 => Monitor(
                        request.ExecutionId, AgentRunStatus.Failed, pollInterval: null,
                        error: new AgentRunError(AgentRunSubtypes.ABORTED)) with
                    { AlreadyRegistered = true },
                _ => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll),
            };
        };
        _stub.OnGetRun = executionId => Monitor(
            executionId, AgentRunStatus.Succeeded, pollInterval: null, result: new AgentRunResult("done", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(),
            PatientOptions(maxPasses: 2, admissionPatience: TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.Equal(2, conclusion.Value.Attempts.Count);
        Assert.Equal(AgentRunSubtypes.ABORTED, conclusion.Value.Attempts[0].FailureSubtype);
        Assert.True(conclusion.Value.Attempts[0].Unacknowledged);
        Assert.Equal("wfexec1_block1_w_2", conclusion.Value.Attempts[^1].ExecutionId);
        Assert.Equal(AgentRunStatus.Succeeded, conclusion.Value.Attempts[^1].Status);
        Assert.False(conclusion.Value.Attempts[^1].Unacknowledged);
    }

    [Fact]
    public async Task ExecuteAsync_MachineryFailureAfterACollectedPass_CarriesTheEvidenceOnTheError()
    {
        var starts = 0;
        _stub.OnStartRun = request =>
        {
            starts++;
            return Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        };
        _stub.OnGetRun = executionId => starts == 1
            ? Monitor(
                executionId, AgentRunStatus.Failed, pollInterval: null,
                error: new AgentRunError(AgentRunSubtypes.INTERNAL_ERROR)) with
            { EstimatedCostUsd = 0.05m }
            : Error.Failure("AgentRun.Unreachable", "socket closed");
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);
        _collector.OnCollect = executionId => new AgentRunTraceCollectionResult(
            true, [new AgentRunTraceFile { Path = $"trace/{executionId}.ndjson", Content = [1] }], []);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 2), TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.POLL_FAILED, conclusion.FirstError.Code);
        Assert.Equal(2, AgentRunAttempts.Of(conclusion.FirstError).Count);
        IReadOnlyList<AgentRunTraceFile> files = AgentRunAttempts.TraceFilesOf(conclusion.FirstError) ?? [];
        Assert.Contains(files, static file => file.Path == "trace/wfexec1_block1_w_1.ndjson");
        Assert.Contains(files, static file => file.Path == "trace/wfexec1_block1_w_2.ndjson");
    }

    [Fact]
    public async Task ExecuteAsync_EarlierWithheldPass_KeepsTheConclusionFlaggedWithheld()
    {
        var starts = 0;
        _stub.OnStartRun = request =>
        {
            starts++;
            return Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        };
        _stub.OnGetRun = executionId => starts == 1
            ? Monitor(
                executionId, AgentRunStatus.Failed, pollInterval: null,
                error: new AgentRunError(AgentRunSubtypes.RUNTIME_SHUTDOWN))
            : Monitor(
                executionId, AgentRunStatus.Succeeded, pollInterval: null,
                result: new AgentRunResult("done", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);
        _collector.OnCollect = executionId => executionId.EndsWith("_w_1", StringComparison.Ordinal)
            ? new AgentRunTraceCollectionResult(false, [], ["fetch failed"])
            : new AgentRunTraceCollectionResult(
                true, [new AgentRunTraceFile { Path = "trace/trace.ndjson", Content = [1] }], []);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 2), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.True(conclusion.Value.CompleteWithheld);
        Assert.Equal(["wfexec1_block1_w_2"], _stub.CompletedExecutionIds);
    }

    [Fact]
    public void ResolvePollInterval_NonPositiveAdvice_FallsBackToTheJitteredInterval()
    {
        AgentRunMonitor zero = Monitor(EXECUTION_ID, AgentRunStatus.Running, TimeSpan.Zero);
        Assert.InRange(
            AgentRunExecutor.ResolvePollInterval(zero), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        AgentRunMonitor advised = Monitor(EXECUTION_ID, AgentRunStatus.Running, TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), AgentRunExecutor.ResolvePollInterval(advised));
    }

    [Fact]
    public async Task ExecuteAsync_RunUnknownToTheRuntime_IsTerminalNoMatterTheTolerance()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = _ => Error.NotFound("AgentRun.NotFound", "no such run");
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(pollErrorTolerance: 5), TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.POLL_FAILED, conclusion.FirstError.Code);
        Assert.Contains("AgentRun.NotFound", conclusion.FirstError.Description);
        _ = Assert.Single(_stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteAsync_RuntimeLosesAStartedRun_ReRegistersAsTheNextPass()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => executionId.EndsWith("_1", StringComparison.Ordinal)
            ? Error.NotFound("AgentRun.NotFound", "no such run")
            : Monitor(
                executionId, AgentRunStatus.Succeeded, pollInterval: null,
                result: new AgentRunResult("survived the restart", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 2), TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError, conclusion.IsError ? conclusion.FirstError.Description : "");
        Assert.True(conclusion.Value.Monitor.TryGetResult(out AgentRunResult? result));
        Assert.Equal("survived the restart", result.Output);
        Assert.Equal(
            ["wfexec1_block1_w_1", "wfexec1_block1_w_2"],
            _stub.StartRequests.Select(static request => request.ExecutionId));
        Assert.Equal(2, conclusion.Value.Attempts.Count);
        Assert.Equal(AgentRunStatus.Running, conclusion.Value.Attempts[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_RunAlreadyReleasedByTheRuntime_IsNotWorthAnotherPass()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = _ => Error.Failure("AgentRun.Completed", "run data released");
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 2), TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.POLL_FAILED, conclusion.FirstError.Code);
        Assert.Contains("AgentRun.Completed", conclusion.FirstError.Description);
        _ = Assert.Single(_stub.StartRequests);
    }

    [Fact]
    public async Task ExecuteAsync_RuntimeLosesEveryPass_FailsWithThePollError()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = _ => Error.NotFound("AgentRun.NotFound", "no such run");
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(), PatientOptions(maxPasses: 2), TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.POLL_FAILED, conclusion.FirstError.Code);
        Assert.Equal(2, _stub.StartRequests.Count);
        var attempts = Assert.IsAssignableFrom<IReadOnlyList<AgentRunAttempt>>(
            conclusion.FirstError.Metadata![AgentRunExecutorErrors.ATTEMPTS_METADATA_KEY]);
        Assert.Equal(2, attempts.Count);
    }

    [Fact]
    public async Task ExecuteAsync_MachineryError_CarriesTheLedgerSoTheBlockCanRecordWhatWasSpent()
    {
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId =>
            Monitor(executionId, AgentRunStatus.Running, TimeSpan.FromMilliseconds(40)) with
            { EstimatedCostUsd = 0.05m };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(),
            PatientOptions(deadline: TimeSpan.FromMilliseconds(120)),
            TestContext.Current.CancellationToken);

        Assert.True(conclusion.IsError);
        Assert.Equal(AgentRunExecutorErrors.DEADLINE_EXCEEDED, conclusion.FirstError.Code);
        var attempts = Assert.IsAssignableFrom<IReadOnlyList<AgentRunAttempt>>(
            conclusion.FirstError.Metadata![AgentRunExecutorErrors.ATTEMPTS_METADATA_KEY]);
        AgentRunAttempt attempt = Assert.Single(attempts);
        Assert.Equal(EXECUTION_ID, attempt.ExecutionId);
        Assert.Equal(0.05m, attempt.EstimatedCostUsd);
    }

    [Fact]
    public async Task AcknowledgeQuietlyAsync_ErrorAndThrowingClient_NeverThrow()
    {
        _stub.OnCompleteRun = _ => Error.Conflict("AgentRun.NotCompletable", "still running");
        await CreateExecutor(_collector).AcknowledgeQuietlyAsync(EXECUTION_ID);

        _stub.OnCompleteRun = _ => throw new HttpRequestException("socket closed");
        await CreateExecutor(_collector).AcknowledgeQuietlyAsync(EXECUTION_ID);

        Assert.Equal([EXECUTION_ID, EXECUTION_ID], _stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task CancelQuietlyAsync_CancelsTheRun()
    {
        _stub.OnCancelRun = executionId => Monitor(executionId, AgentRunStatus.Cancelling, _instantPoll);

        await CreateExecutor(_collector).CancelQuietlyAsync(EXECUTION_ID);

        Assert.Equal([EXECUTION_ID], _stub.CancelledExecutionIds);
    }

    [Fact]
    public async Task CancelQuietlyAsync_ErrorAndThrowingClient_NeverThrow()
    {
        _stub.OnCancelRun = _ => Error.NotFound("AgentRun.NotFound", "no such run");
        await CreateExecutor(_collector).CancelQuietlyAsync(EXECUTION_ID);

        _stub.OnCancelRun = _ => throw new HttpRequestException("socket closed");
        await CreateExecutor(_collector).CancelQuietlyAsync(EXECUTION_ID);

        Assert.Equal([EXECUTION_ID, EXECUTION_ID], _stub.CancelledExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_OnRunStarted_IsAwaitedBetweenStartAndFirstPoll()
    {
        var startedRuns = new List<AgentRunStarted>();
        ArrangeSettlingRun(
            AgentRunStatus.Succeeded, result: new AgentRunResult("agent output", null), recordEvents: true);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(),
            _patientOptions with
            {
                OnRunStarted = (started, _) =>
                {
                    _events.Add("bookkeeping");
                    startedRuns.Add(started);
                    return Task.CompletedTask;
                },
            },
            TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        AgentRunStarted started = Assert.Single(startedRuns);
        Assert.Equal(1, started.Pass);
        Assert.Equal(EXECUTION_ID, started.ExecutionId);
        Assert.Equal(["start", "bookkeeping", "poll"], _events.Take(3));
    }

    [Fact]
    public async Task ExecuteAsync_OnRunStarted_FiresForEveryPassWithItsOwnId()
    {
        var startedRuns = new List<AgentRunStarted>();
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId => executionId.EndsWith("_1", StringComparison.Ordinal)
            ? Monitor(
                executionId, AgentRunStatus.Failed, pollInterval: null,
                error: new AgentRunError(AgentRunSubtypes.RUNTIME_SHUTDOWN))
            : Monitor(
                executionId, AgentRunStatus.Succeeded, pollInterval: null,
                result: new AgentRunResult("second pass output", null));
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Succeeded, pollInterval: null);

        ErrorOr<AgentRunConclusion> conclusion = await CreateExecutor(_collector).ExecuteAsync(
            Request(),
            PatientOptions(maxPasses: 3) with
            {
                OnRunStarted = (started, _) =>
                {
                    startedRuns.Add(started);
                    return Task.CompletedTask;
                },
            },
            TestContext.Current.CancellationToken);

        Assert.False(conclusion.IsError);
        Assert.Equal([1, 2], startedRuns.Select(static started => started.Pass));
        Assert.Equal(
            ["wfexec1_block1_w_1", "wfexec1_block1_w_2"],
            startedRuns.Select(static started => started.ExecutionId));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowingOnRunStarted_CancelsTheFreshRunRecordsItsAttemptAndPropagates()
    {
        var observed = new List<AgentRunAttempt>();
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateExecutor(_collector).ExecuteAsync(
                Request(),
                _patientOptions with
                {
                    OnAttempt = observed.Add,
                    OnRunStarted = (_, _) => throw new OperationCanceledException(),
                },
                TestContext.Current.CancellationToken));

        Assert.Equal([EXECUTION_ID], _stub.CancelledExecutionIds);
        AgentRunAttempt attempt = Assert.Single(observed);
        Assert.Equal(EXECUTION_ID, attempt.ExecutionId);
        Assert.True(attempt.Unacknowledged);
        Assert.Empty(_stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_AnchoredRun_PersistsTheStampedEvidenceBeforeAcknowledging()
    {
        var persister = new RecordingPersister { Events = _events };
        _collector.Events = _events;
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson", "trace/model.ndjson");
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, persister: persister)
            .ExecuteAsync(Request(), AnchoredOptions(), TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.Equal(["collect", "persist", "complete"], _events);
        IReadOnlyList<AgentRunTraceFile> persisted = Assert.Single(persister.Persisted);
        Assert.Equal(2, persisted.Count);
        Assert.All(persisted, static file =>
        {
            Assert.Equal("org-1", file.OrganizationId);
            Assert.Equal("block-1", file.BlockId);
        });
        Assert.False(concluded.Value.CompleteWithheld);
        Assert.False(Assert.Single(concluded.Value.Attempts).Unacknowledged);
    }

    [Fact]
    public async Task ExecuteAsync_FailedPersistence_WithholdsAcknowledgeAndKeepsThePack()
    {
        var persister = new RecordingPersister { Result = false };
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, persister: persister)
            .ExecuteAsync(Request(), AnchoredOptions(), TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.Empty(_stub.CompletedExecutionIds);
        Assert.True(concluded.Value.CompleteWithheld);
        Assert.True(Assert.Single(concluded.Value.Attempts).Unacknowledged);
        Assert.NotNull(concluded.Value.TraceFiles);
    }

    [Fact]
    public async Task ExecuteAsync_FailedPersistence_LogsTheExecutionId()
    {
        var logger = new RecordingLogger();
        var persister = new RecordingPersister { Result = false };
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        _ = await CreateExecutor(_collector, persister: persister, logger: logger)
            .ExecuteAsync(Request(), AnchoredOptions(), TestContext.Current.CancellationToken);

        Assert.Contains(logger.Messages, message =>
            message.Contains(EXECUTION_ID) && message.Contains("withholding complete"));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowingPersister_WithholdsAcknowledge()
    {
        var persister = new RecordingPersister { Throw = true };
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, persister: persister)
            .ExecuteAsync(Request(), AnchoredOptions(), TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.Empty(_stub.CompletedExecutionIds);
        Assert.True(concluded.Value.CompleteWithheld);
    }

    [Fact]
    public async Task ExecuteAsync_AnchoredRunWithoutAPersister_WithholdsAcknowledge()
    {
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector)
            .ExecuteAsync(Request(), AnchoredOptions(), TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.Empty(_stub.CompletedExecutionIds);
        Assert.True(concluded.Value.CompleteWithheld);
    }

    [Fact]
    public async Task ExecuteAsync_UnanchoredRun_AcknowledgesOnCollectionAloneAndNeverPersists()
    {
        var persister = new RecordingPersister();
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, persister: persister)
            .ExecuteAsync(Request(), _patientOptions, TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
        Assert.Empty(persister.Persisted);
        Assert.False(concluded.Value.CompleteWithheld);
    }

    [Fact]
    public async Task ExecuteAsync_AnchoredZeroRowCollection_AcknowledgesWithoutAPersistCall()
    {
        var persister = new RecordingPersister();
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, persister: persister)
            .ExecuteAsync(Request(), AnchoredOptions(), TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
        Assert.Empty(persister.Persisted);
    }

    [Fact]
    public async Task ExecuteAsync_FailedCollectionWithPartialRows_PersistsTheEvidenceAndStillWithholds()
    {
        var persister = new RecordingPersister();
        _collector.OnCollect = _ => new AgentRunTraceCollectionResult(
            false,
            [new AgentRunTraceFile { Path = "trace/trace.ndjson" }],
            ["fetch failed for trace/sub.ndjson"]);
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, persister: persister)
            .ExecuteAsync(Request(), AnchoredOptions(), TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.Empty(_stub.CompletedExecutionIds);
        _ = Assert.Single(persister.Persisted);
        Assert.True(concluded.Value.CompleteWithheld);
    }

    [Fact]
    public async Task SettleAbandonedQuietlyAsync_SettlesCollectsPersistsAndAcknowledges()
    {
        var persister = new RecordingPersister { Events = _events };
        _collector.Events = _events;
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        _stub.OnCancelRun = executionId =>
        {
            _events.Add("cancel");
            return Monitor(executionId, AgentRunStatus.Cancelling, _instantPoll);
        };
        _stub.OnGetRun = executionId =>
        {
            _events.Add("settle-poll");
            return Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
        };
        _stub.OnCompleteRun = executionId =>
        {
            _events.Add("complete");
            return Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
        };

        bool acknowledged = await CreateExecutor(_collector, persister: persister)
            .SettleAbandonedQuietlyAsync(EXECUTION_ID, _anchor);

        Assert.True(acknowledged);
        Assert.Equal(["cancel", "settle-poll", "collect", "persist", "complete"], _events);
        IReadOnlyList<AgentRunTraceFile> persisted = Assert.Single(persister.Persisted);
        Assert.Equal("org-1", Assert.Single(persisted).OrganizationId);
    }

    [Fact]
    public async Task SettleAbandonedQuietlyAsync_FailedPersistence_WithholdsTheAcknowledge()
    {
        var persister = new RecordingPersister { Result = false };
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        _stub.OnCancelRun = executionId => Monitor(executionId, AgentRunStatus.Cancelling, _instantPoll);
        _stub.OnGetRun = executionId => Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);

        bool acknowledged = await CreateExecutor(_collector, persister: persister)
            .SettleAbandonedQuietlyAsync(EXECUTION_ID, _anchor);

        Assert.False(acknowledged);
        Assert.Empty(_stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledEphemeralRun_WithholdsSoTheRuntimeKeepsThePack()
    {
        using var cancellation = new CancellationTokenSource();
        bool cancelled = false;
        _stub.OnStartRun = request => Monitor(request.ExecutionId, AgentRunStatus.Running, _instantPoll);
        _stub.OnGetRun = executionId =>
        {
            if (cancelled)
            {
                return Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
            }

            cancellation.Cancel();
            return Monitor(executionId, AgentRunStatus.Running, TimeSpan.FromMilliseconds(1));
        };
        _stub.OnCancelRun = executionId =>
        {
            cancelled = true;
            return Monitor(executionId, AgentRunStatus.Cancelling, _instantPoll);
        };
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        var observed = new List<AgentRunAttempt>();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateExecutor(_collector).ExecuteAsync(
                Request(),
                _patientOptions with { OnAttempt = observed.Add },
                cancellation.Token));

        Assert.Empty(_stub.CompletedExecutionIds);
        Assert.True(Assert.Single(observed).Unacknowledged);
    }

    [Fact]
    public async Task SettleAbandonedQuietlyAsync_RunAlreadyReleasedByTheRuntime_WithholdsWithoutThrowing()
    {
        _stub.OnCancelRun = executionId => Error.NotFound(
            AgentRunLifecycleErrors.NOT_FOUND, "no such run");
        _stub.OnGetRun = executionId => Error.NotFound(
            AgentRunLifecycleErrors.COMPLETED, "run completed and released");
        _collector.OnCollect = _ => new AgentRunTraceCollectionResult(
            false, [], ["listing answered 410 completed"]);

        bool acknowledged = await CreateExecutor(_collector)
            .SettleAbandonedQuietlyAsync(EXECUTION_ID, _anchor);

        Assert.False(acknowledged);
        Assert.Empty(_stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task SettleAbandonedQuietlyAsync_ThrowingClient_NeverThrows()
    {
        _stub.OnCancelRun = _ => throw new HttpRequestException("socket closed");
        _stub.OnGetRun = _ => throw new HttpRequestException("socket closed");
        _stub.OnCompleteRun = _ => throw new HttpRequestException("socket closed");

        bool acknowledged = await CreateExecutor(_collector)
            .SettleAbandonedQuietlyAsync(EXECUTION_ID, anchor: null);

        Assert.False(acknowledged);
    }

    private static readonly AgentRunSessionBundleAnchor _bundleAnchor = AgentRunSessionBundleAnchor.ForBlock("org-1", "block-1", 1);

    [Fact]
    public async Task ExecuteAsync_RunInAVerificationGroup_TakesBundleCustodyAfterTheGatingEvidenceAndBeforeAcknowledging()
    {
        var custodian = new RecordingCustodian { Events = _events };
        var persister = new RecordingPersister { Events = _events };
        _collector.Events = _events;
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, persister: persister, custodian: custodian)
            .ExecuteAsync(
                Request(),
                AnchoredOptions() with { SessionBundleAnchor = _bundleAnchor },
                TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        // The evidence that can withhold the acknowledgement runs first; custody, which never
        // withholds, goes after it and still lands before the acknowledgement releases the copy.
        Assert.Equal(["collect", "persist", "custody", "complete"], _events);
        (AgentRunSessionBundleAnchor anchor, string executionId, int attempt, int droppedOutFileCount) =
            Assert.Single(custodian.Taken);
        Assert.Equal(_bundleAnchor, anchor);
        Assert.Equal(EXECUTION_ID, executionId);
        Assert.Equal(1, attempt);
        Assert.Equal(0, droppedOutFileCount);
    }

    /// <summary>
    /// The whole point of the gate: a block outside a verification group can never be sent back
    /// to redo its work, so nothing is fetched and nothing is stored for it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RunOutsideAnyVerificationGroup_TakesNoBundleCustody()
    {
        var custodian = new RecordingCustodian();
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, custodian: custodian)
            .ExecuteAsync(Request(), AnchoredOptions(), TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.Empty(custodian.Taken);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
    }

    /// <summary>
    /// Custody fails open. A lost bundle costs a redo leg its head start; a withheld
    /// acknowledgement holds runtime retention capacity for the whole window.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_BundleCustodyFails_StillAcknowledgesTheRun()
    {
        var custodian = new RecordingCustodian { Result = false };
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, custodian: custodian)
            .ExecuteAsync(
                Request(),
                AnchoredOptions() with { SessionBundleAnchor = _bundleAnchor },
                TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.False(concluded.Value.CompleteWithheld);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_BundleCustodyThrows_StillAcknowledgesTheRun()
    {
        var custodian = new RecordingCustodian { Throw = true };
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, custodian: custodian)
            .ExecuteAsync(
                Request(),
                AnchoredOptions() with { SessionBundleAnchor = _bundleAnchor },
                TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.False(concluded.Value.CompleteWithheld);
        Assert.Equal([EXECUTION_ID], _stub.CompletedExecutionIds);
    }

    /// <summary>
    /// A failed persist withholds the acknowledgement AND skips custody: the persisted out/
    /// rows are the payload a continuation re-carries, so a bundle held beside rows that never
    /// reached the database would resume the worker into a workspace missing its own files.
    /// No custody row means the planner's no-bundle scratch — the safe answer.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TracePersistFails_WithholdsTheAcknowledgementAndSkipsCustody()
    {
        var custodian = new RecordingCustodian();
        var persister = new RecordingPersister { Result = false };
        _collector.OnCollect = _ => CollectedRows("trace/trace.ndjson");
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, persister: persister, custodian: custodian)
            .ExecuteAsync(
                Request(),
                AnchoredOptions() with { SessionBundleAnchor = _bundleAnchor },
                TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.True(concluded.Value.CompleteWithheld);
        Assert.Empty(custodian.Taken);
        Assert.Empty(_stub.CompletedExecutionIds);
    }

    /// <summary>
    /// A failed listing leaves the run's out/ set unknowable, so a stored bundle would be a
    /// resume trap — the platform could not know the re-carry set is complete. No custody row
    /// converts the case into the planner's no-bundle scratch, while the withheld
    /// acknowledgement keeps the runtime's copy collectable.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TraceCollectionFails_SkipsBundleCustody()
    {
        var custodian = new RecordingCustodian();
        _collector.OnCollect = _ => new AgentRunTraceCollectionResult(false, [], ["listing failed"]);
        ArrangeSettlingRun(AgentRunStatus.Succeeded, new AgentRunResult("done", null));

        ErrorOr<AgentRunConclusion> concluded = await CreateExecutor(_collector, custodian: custodian)
            .ExecuteAsync(
                Request(),
                AnchoredOptions() with { SessionBundleAnchor = _bundleAnchor },
                TestContext.Current.CancellationToken);

        Assert.False(concluded.IsError);
        Assert.Empty(custodian.Taken);
    }

    /// <summary>
    /// Every teardown path abandons its run, and the execution that owned it is on its way to a
    /// terminal that releases bundles anyway.
    /// </summary>
    [Fact]
    public async Task SettleAbandonedQuietlyAsync_AbandonedRun_TakesNoBundleCustody()
    {
        var custodian = new RecordingCustodian();
        _stub.OnCancelRun = executionId => Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
        _stub.OnGetRun = executionId => Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);
        _stub.OnCompleteRun = executionId => Monitor(executionId, AgentRunStatus.Cancelled, pollInterval: null);

        _ = await CreateExecutor(_collector, custodian: custodian)
            .SettleAbandonedQuietlyAsync(EXECUTION_ID, _anchor);

        Assert.Empty(custodian.Taken);
    }
}
