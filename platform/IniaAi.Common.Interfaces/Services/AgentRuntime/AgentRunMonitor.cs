using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Status monitor returned by every agent runtime endpoint — the single wire shape
/// for started, polled, cancelled, and input-resumed runs. Prefer the TryGet accessors
/// over the raw optional properties: they encode the sidecar's status→payload invariants
/// (result only on succeeded, error only on failed, requiredInput only on awaiting_input),
/// so consumers branch on one status check instead of null-checking four optionals.
/// </summary>
public sealed record AgentRunMonitor
{
    [JsonRequired]
    public string ExecutionId { get; init; } = string.Empty;

    public int Attempt { get; init; }

    public int Segment { get; init; }

    /// <summary>
    /// The member-level converter attribute is load-bearing: serializer options that carry their
    /// own enum converter (JsonDefaults.Options does) override a type-level attribute, but never
    /// a member-level one.
    /// </summary>
    [JsonRequired]
    [JsonConverter(typeof(SnakeCaseEnumJsonConverter<AgentRunStatus>))]
    public AgentRunStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public decimal EstimatedCostUsd { get; init; }

    public AgentRunError? Error { get; init; }

    public AgentRunResult? Result { get; init; }

    public AgentRunRequiredInput? RequiredInput { get; init; }

    public DateTimeOffset? PausedAt { get; init; }

    public DateTimeOffset? ResumedAt { get; init; }

    public long? LastPauseMs { get; init; }

    /// <summary>
    /// The fuses the runtime registered for this run, declared or defaulted — its authoritative
    /// per-run record. Null from a runtime that predates the echo.
    /// </summary>
    public AgentRunFuses? Fuses { get; init; }

    /// <summary>
    /// Poll interval the sidecar asked for via the Retry-After header. The contract sends it on
    /// every non-terminal monitor and never on a terminal one, so null is the stop-polling signal.
    /// Transport metadata, not part of the JSON body.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? PollInterval { get; init; }

    /// <summary>
    /// True when StartRunAsync hit an idempotent duplicate (HTTP 200) instead of
    /// registering a new run (HTTP 202). Transport metadata, not part of the JSON body.
    /// </summary>
    [JsonIgnore]
    public bool AlreadyRegistered { get; init; }

    /// <summary>
    /// True only when the run is awaiting input and carries the pause payload.
    /// </summary>
    public bool TryGetRequiredInput([NotNullWhen(true)] out AgentRunRequiredInput? requiredInput)
    {
        requiredInput = Status == AgentRunStatus.AwaitingInput ? RequiredInput : null;
        return requiredInput is not null;
    }

    /// <summary>
    /// True only when the run succeeded and carries its result payload.
    /// </summary>
    public bool TryGetResult([NotNullWhen(true)] out AgentRunResult? result)
    {
        result = Status == AgentRunStatus.Succeeded ? Result : null;
        return result is not null;
    }

    /// <summary>
    /// True only when the run failed and carries its error payload.
    /// </summary>
    public bool TryGetError([NotNullWhen(true)] out AgentRunError? error)
    {
        error = Status == AgentRunStatus.Failed ? Error : null;
        return error is not null;
    }
}

/// <summary>
/// The monitor's error payload: only the machine-readable failure subtype — see
/// <see cref="AgentRunSubtypes"/> for the vocabulary. The sidecar deliberately sends no
/// prose message on the monitor.
/// </summary>
public sealed record AgentRunError(string? Subtype);

public sealed record AgentRunFuses(decimal? MaxBudgetUsd);

public sealed record AgentRunResult(string? Output, string? SessionId);

/// <summary>
/// The pause payload of an awaiting_input run: one resume token and exactly one question.
/// </summary>
public sealed record AgentRunRequiredInput(
    string ResumeToken,
    DateTimeOffset ExpiresAt,
    AgentRunQuestion Question);

/// <summary>
/// A single question the worker paused on. Input is the worker's structured draft — an
/// arbitrary JSON object whose shape belongs to the pausing tool, kept as raw JSON on purpose.
/// </summary>
public sealed record AgentRunQuestion(string Id, string ToolName, string Prompt, JsonElement Input);
