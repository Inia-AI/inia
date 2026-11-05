namespace IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

/// <summary>
/// The client-facing projection of a run's trace pack: ordered activity beats, the phase
/// vocabulary they reference, and receipt facts. This shape is the API contract — it cannot
/// represent workspace paths, run/session ids, raw commands, token counts, or cost, so
/// nothing internal-shaped can reach a client by construction. Domain flavour enters only
/// through run data (skill names, document names, agent narration); the mapping's own labels
/// stay domain-neutral (<see cref="AgentActivityMapping"/>).
/// </summary>
public sealed record AgentActivityDigest
{
    public const int CURRENT_SCHEMA_VERSION = 1;

    public int SchemaVersion { get; init; } = CURRENT_SCHEMA_VERSION;

    public AgentActivityOutcome Outcome { get; init; }

    /// <summary>
    /// The agent run's own absolute start — the anchor every beat's
    /// <see cref="AgentActivityBeat.AtSeconds"/> is measured from. Null when the pack carries
    /// nothing timestamped. This is the run's start, not the workflow execution's: an agent
    /// block typically begins minutes into its execution.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; init; }

    public IReadOnlyList<AgentActivityPhase> Phases { get; init; } = [];

    public IReadOnlyList<AgentActivityBeat> Beats { get; init; } = [];

    public AgentActivityReceipt Receipt { get; init; } = new();
}

/// <summary>
/// How the run stands as of the projected trace: still running (no terminal stage record
/// yet), settled well, or settled badly. Unknown covers a pack whose stage records never
/// arrived — projection degrades, never errors.
/// </summary>
public enum AgentActivityOutcome
{
    Unknown = 0,
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// One entry of the phase vocabulary. <paramref name="GapLabel"/> is the honest reading
/// state a client shows between beats while this phase is current.
/// </summary>
public sealed record AgentActivityPhase(string Label, string GapLabel);

/// <summary>
/// Hero beats mark the moments the capability lands (the agent ran code it wrote; it looked
/// at a page with its own eyes); check beats mark a verification the agent itself reported.
/// Everything else is Normal.
/// </summary>
public enum AgentActivityBeatKind
{
    Unknown = 0,
    Normal,
    Hero,
    Check,
}

/// <summary>
/// One paced feed line. <paramref name="AtSeconds"/> is the offset from the run's start —
/// never an absolute timestamp, so replays pace themselves without knowing wall time.
/// <paramref name="PhaseIndex"/> points into <see cref="AgentActivityDigest.Phases"/>.
/// </summary>
public sealed record AgentActivityBeat(
    int AtSeconds,
    int PhaseIndex,
    AgentActivityBeatKind Kind,
    string Label,
    string? Detail)
{
    /// <summary>
    /// The staged documents the step read or searched, by file name, so a consumer can name a
    /// read of its own files in its own words. Empty for a step that touched none.
    /// </summary>
    public IReadOnlyList<string> DocumentNames { get; init; } = [];

    /// <summary>The consumer's icon key for a step its labeller named; null otherwise.</summary>
    public string? Icon { get; init; }

    public bool Equals(AgentActivityBeat? other)
    {
        return other is not null
            && AtSeconds == other.AtSeconds
            && PhaseIndex == other.PhaseIndex
            && Kind == other.Kind
            && Label == other.Label
            && Detail == other.Detail
            && Icon == other.Icon
            && DocumentNames.SequenceEqual(other.DocumentNames);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(AtSeconds, PhaseIndex, Kind, Label, Detail, DocumentNames.Count);
    }
}

/// <summary>
/// The receipt facts a settled feed collapses into. All values derive from the run's own
/// data; counts are zero and lists empty until the trace carries the underlying records.
/// </summary>
public sealed record AgentActivityReceipt
{
    public int? DurationSeconds { get; init; }

    public IReadOnlyList<string> DocumentNames { get; init; } = [];

    public IReadOnlyList<string> SkillNames { get; init; } = [];

    public int PagesViewed { get; init; }

    public int CodeRuns { get; init; }

    public int ChecksReported { get; init; }
}
