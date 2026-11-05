using ErrorOr;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Typed HTTP client for the agent runtime sidecar: start, poll, cancel, and resume agent runs.
/// Single-shot verbs only — anything loop-shaped (poll to settled, the collect-then-acknowledge
/// lifecycle) belongs to <see cref="IAgentRunExecutor"/>, which owns the one polling
/// implementation and its process-wide gate. Every call carries the static service bearer
/// token — unless the dev-only AgentRuntimeSettings.AllowUnauthenticatedSidecar opt-out is set,
/// for a local sidecar running without auth. A run additionally carries the per-run JWT minted
/// at start (the <c>runToken</c> body field). Execution ids must already match the sidecar's
/// <c>^[A-Za-z0-9._-]{1,128}$</c> shape — mint them with <see cref="AgentRunIds.For"/>; an
/// illegal id fails validation before any HTTP is attempted.
/// </summary>
public interface IAgentRuntimeClient
{
    /// <summary>
    /// Registers a run on the sidecar. Idempotent: re-posting the same executionId and prompt
    /// returns the existing run's monitor with <see cref="AgentRunMonitor.AlreadyRegistered"/> set;
    /// the same executionId with a different payload is an AgentRun.PayloadConflict.
    /// </summary>
    Task<ErrorOr<AgentRunMonitor>> StartRunAsync(
        string executionId,
        string prompt,
        string runToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a run with the full content-channel payload: input documents, the worker's
    /// skill-file bundle, the model, and the (currently unused) MCP server URL. Same idempotency
    /// contract as the three-argument overload; the same executionId with different documents is
    /// an AgentRun.PayloadConflict. Inputs are pre-validated — staged-input name shape,
    /// uniqueness, reserved names, and the runtime's 200 MiB decoded byte budget — and fail with
    /// AgentRun.InvalidDocument / AgentRun.DocumentsOverBudget before any HTTP is attempted.
    /// </summary>
    Task<ErrorOr<AgentRunMonitor>> StartRunAsync(
        AgentRunStartRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls a run's status monitor. Honour <see cref="AgentRunMonitor.PollInterval"/> as the
    /// interval before the next poll; it is null exactly when the run is terminal (stop polling).
    /// Terminal runs stay queryable for at least 24 hours.
    /// </summary>
    Task<ErrorOr<AgentRunMonitor>> GetRunAsync(string executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests best-effort cancellation. Returns the monitor — status "cancelling" when accepted,
    /// or the terminal monitor when the run already finished.
    /// <para>
    /// <b>Cancellation-token trap:</b> never pass the workflow's own dying token here. Cancel is
    /// usually called precisely because that token is being cancelled, and an already-cancelled
    /// token makes the HTTP layer throw <see cref="OperationCanceledException"/> before the request
    /// ever reaches the wire — the sidecar keeps running and keeps billing. Call with a fresh,
    /// short-lived token instead:
    /// <code>
    /// using var cancelTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    /// await client.CancelRunAsync(executionId, cancelTimeout.Token);
    /// </code>
    /// </para>
    /// </summary>
    Task<ErrorOr<AgentRunMonitor>> CancelRunAsync(string executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers the single question of a run awaiting input. allowFurtherPause=false is the release
    /// verb: the worker must finish from this answer and may not park again. The 409 outcomes map
    /// to distinct codes — AgentRun.StaleResumeToken, AgentRun.NotAwaitingInput,
    /// AgentRun.UnknownQuestion — each carrying the run's current status in metadata when known.
    /// </summary>
    Task<ErrorOr<AgentRunMonitor>> SubmitInputAsync(
        string executionId,
        string resumeToken,
        string questionId,
        string answer,
        bool allowFurtherPause = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the run's current file manifest — published deliverables and trace files. Available
    /// while the run exists, including mid-run (the manifest swaps atomically on each publish);
    /// after a successful complete the data is gone and the call maps to AgentRun.Completed.
    /// </summary>
    Task<ErrorOr<IReadOnlyList<AgentRunFileEntry>>> ListRunFilesAsync(
        string executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches one manifest file's raw bytes by its manifest path. Unknown paths are
    /// AgentRun.NotFound; a completed run's files are gone and map to AgentRun.Completed.
    /// </summary>
    Task<ErrorOr<byte[]>> GetRunFileAsync(
        string executionId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the terminal session bundle of a settled run, with the digest the runtime holds
    /// for it. A run that published none — it failed before its first model turn, or ran on a
    /// sidecar older than the bundle — is AgentRun.NotFound, which is an ordinary answer here
    /// and not a fault. A completed run's data is gone and maps to AgentRun.Completed, so this
    /// is only fetchable before the acknowledgement releases the runtime's copy.
    /// </summary>
    Task<ErrorOr<AgentRunCheckpoint>> GetRunCheckpointAsync(
        string executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumer-acknowledged teardown: the runtime purges the run's data before answering 200,
    /// so a success here guarantees the data is gone. Only terminal runs are completable — a
    /// non-terminal run maps to AgentRun.NotCompletable with the current status in metadata.
    /// The monitor stays queryable after complete; only the files are gone.
    /// </summary>
    Task<ErrorOr<AgentRunMonitor>> CompleteRunAsync(
        string executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the runtime's deployed per-run fuse limits (<c>GET /v1/limits</c> — default,
    /// ceiling and unit per fuse). Discovery only: the run-start 400 stays the enforcement and
    /// the start response's effective-fuse echo outranks this view. Callers go through the
    /// cached <see cref="IAgentRunLimitsProvider"/> rather than fetching per decision.
    /// </summary>
    Task<ErrorOr<AgentRunLimits>> GetLimitsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the runtime's environment text (<c>GET /v1/environment</c>). A runtime older than
    /// the endpoint answers 404, which maps to <see cref="AgentRuntimeEnvironmentErrors.NOT_SERVED"/>.
    /// Callers go through the cached <see cref="IAgentRuntimeEnvironmentProvider"/>.
    /// </summary>
    Task<ErrorOr<AgentRuntimeEnvironment>> GetEnvironmentAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Codes for a 503 refusal: the runtime declined to take the request on — a capacity shed, a
/// byte- or upload-budget exhaustion, or a shutdown drain. Runs occupy the pool for minutes,
/// so these are not transient faults the transport can retry away; callers re-dispatch from
/// their own durable layer instead.
/// <para>
/// <b>A refusal does not prove the run is unregistered.</b> The runtime reserves its output
/// and input budgets <i>after</i> <c>register</c> succeeds, so a refused start can still have
/// left a failed record for the retention window. A re-post of that executionId with a
/// changed payload answers AgentRun.PayloadConflict, and a status probe answers a monitor
/// rather than 404 — a re-dispatcher must handle both instead of assuming the id is free.
/// A 503 also reaches every verb, not only start.
/// </para>
/// <para>
/// When the refusal carried a usable Retry-After, the honoured delay — the server value
/// capped, then jittered upward only, because Retry-After is a floor (RFC 9110 §10.2.3) and
/// returning early just earns another refusal — rides under
/// <see cref="RETRY_AFTER_METADATA_KEY"/> as a <see cref="TimeSpan"/>. A missing key means
/// the runtime advised nothing usable and the caller falls back to its own backoff.
/// </para>
/// </summary>
public static class AgentRunAdmissionErrors
{
    public const string CAPACITY = "AgentRun.Capacity";
    public const string SHUTTING_DOWN = "AgentRun.ShuttingDown";
    public const string RETRY_AFTER_METADATA_KEY = "retryAfter";

    /// <summary>
    /// The runtime has chat admission switched off (a chat cap of 0, reason <c>chat_disabled</c>
    /// on the 503). Not an admission refusal: waiting cannot help until an operator changes the
    /// runtime, so no caller re-posts on it.
    /// </summary>
    public const string CHAT_DISABLED = "AgentRun.ChatDisabled";

    /// <summary>
    /// The one definition of "the runtime refused to admit the start". The executor's patience
    /// re-posts the same request instance on exactly these codes, and the client keeps its
    /// serialized body cached for exactly as long — a code added to one side but not the other
    /// would re-post a body that was already released.
    /// </summary>
    public static bool IsAdmissionRefusal(string errorCode)
    {
        return errorCode is CAPACITY or SHUTTING_DOWN;
    }
}

/// <summary>
/// The client error codes that answer a question about the run itself rather than about the
/// call. A poller tolerating transport hiccups must still stop on these: they say the run is
/// gone (404) or its data released (410), and no amount of patience brings either back.
/// </summary>
public static class AgentRunLifecycleErrors
{
    public const string NOT_FOUND = "AgentRun.NotFound";
    public const string COMPLETED = "AgentRun.Completed";
}
