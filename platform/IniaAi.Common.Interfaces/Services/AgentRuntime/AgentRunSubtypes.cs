namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The sidecar's failure subtype vocabulary (its RunFailureSubtype union), mirrored so callers
/// branch on constants instead of scattering wire literals. The wire type is open — the sidecar
/// may add subtypes — so always treat an unrecognised subtype as non-retryable.
/// </summary>
public static class AgentRunSubtypes
{
    public const string ABORTED = "aborted";
    public const string BUDGET_EXHAUSTED = "budget_exhausted";
    public const string CHECKPOINT_FAILED = "checkpoint_failed";
    public const string CHECKPOINT_MISSING = "checkpoint_missing";
    public const string CHECKPOINT_RESTORE_FAILED = "checkpoint_restore_failed";
    public const string CHECKPOINT_VERSION_MISMATCH = "checkpoint_version_mismatch";
    public const string DEADLINE_EXCEEDED = "deadline_exceeded";
    public const string ERRORED_RESULT = "errored_result";
    public const string INTERNAL_ERROR = "internal_error";
    public const string MALFORMED_PAUSE = "malformed_pause";
    public const string MALFORMED_RESULT = "malformed_result";
    public const string MCP_CONNECT_FAILED = "mcp_connect_failed";
    public const string MISSING_RESULT = "missing_result";
    public const string PAUSE_REJECTED = "pause_rejected";
    public const string PAUSE_UNSUPPORTED = "pause_unsupported";
    public const string RUN_CHILD_ERROR = "run_child_error";
    public const string RUNTIME_SHUTDOWN = "runtime_shutdown";
    public const string SESSION_ID_MISMATCH = "session_id_mismatch";
    public const string UID_POOL_EXHAUSTED = "uid_pool_exhausted";
    public const string UPSTREAM_ERROR = "upstream_error";

    /// <summary>
    /// Whether re-registering the run with a fresh executionId pass is a sensible reaction to this
    /// failure. Deliberately conservative — only infrastructure-level failures where the sidecar
    /// itself broke or lacked capacity qualify, and money spent proves the run reached the model,
    /// so a spend-carrying failure is never re-bought:
    /// <list type="bullet">
    ///   <item><description><see cref="UID_POOL_EXHAUSTED"/> — transient capacity, retry later.</description></item>
    ///   <item><description><see cref="INTERNAL_ERROR"/> — the runtime broke, not the run; a fresh
    ///   attempt may succeed.</description></item>
    ///   <item><description><see cref="RUNTIME_SHUTDOWN"/> — the sidecar drained on a signal and
    ///   failed its in-flight runs because they cannot survive the process. The one deliberate
    ///   exception to the spend rule: the drained run may carry spend, but a deploy is the
    ///   platform's own infrastructure at work, and a routine deploy must not permanently fail
    ///   the block.</description></item>
    /// </list>
    /// <see cref="RUN_CHILD_ERROR"/> is excluded: the runtime uses it as its non-retryable,
    /// spend-carrying failure — including its $0 catch-all, as often deterministic as not — so a
    /// fresh pass would re-buy a run the runtime told the platform not to re-run.
    /// <see cref="ABORTED"/> is excluded: it can reflect a deliberate operator stop, and
    /// auto-retrying would resurrect runs someone chose to kill. Deterministic outcomes (deadline,
    /// budget, checkpoint, pause and result shapes) never retry — the same run would fail the same
    /// way. <see cref="ERRORED_RESULT"/> is excluded because the in-band error it carries may be
    /// deterministic (a refusal) as easily as transient, and <see cref="MCP_CONNECT_FAILED"/>
    /// because an unreachable tool server is as likely misconfigured as briefly down. Unknown
    /// subtypes are non-retryable by default. <see cref="UPSTREAM_ERROR"/> is excluded too: a pass
    /// bought at once lands inside the same outage, so it has its own delayed, spend-bounded rule
    /// (<see cref="IsUpstreamOutage"/>).
    /// </summary>
    public static bool IsRetryable(string subtype)
    {
        return subtype is UID_POOL_EXHAUSTED or INTERNAL_ERROR or RUNTIME_SHUTDOWN;
    }

    /// <summary>
    /// The model API failed on its side (runtime ADR 0130). Worth one fresh pass after a wait, under
    /// the caller's spend bound (<see cref="AgentRunExecutionOptions.UpstreamErrorRetryDelay"/>),
    /// never a pass bought back to back.
    /// </summary>
    public static bool IsUpstreamOutage(string subtype)
    {
        return subtype is UPSTREAM_ERROR;
    }

    /// <summary>
    /// The three ways a continuation start can fail to restore the session it was sent:
    /// an undecodable or unrestorable bundle, a bundle whose format this sidecar
    /// does not read, and a resumed stream reporting a different session. A broken bundle
    /// restores broken every time, so none is retryable — what a consumer may do instead is
    /// degrade the leg to a scratch start, and only when the failure carried no spend (the
    /// dispatch layer owns that rule).
    /// </summary>
    public static bool IsContinuationRestoreFailure(string subtype)
    {
        return subtype is CHECKPOINT_RESTORE_FAILED or CHECKPOINT_VERSION_MISMATCH or SESSION_ID_MISMATCH;
    }
}
