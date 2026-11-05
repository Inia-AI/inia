namespace IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

/// <summary>
/// Which channel supplies the feed's sentences. The agent's own narration is the spine —
/// its words cannot misattribute intent, where a sentence authored from a command is our
/// guess at why the command ran. The classified channel survives as the degraded path for
/// narration-thin runs, and stays independently selectable so the same trace can be
/// rendered under either approach and judged side by side.
/// </summary>
public enum AgentActivityBeatSource
{
    /// <summary>
    /// Narrated when the run narrated enough to carry a feed; classified otherwise.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Beat sentences come from the agent's own text; tool calls contribute structure
    /// (phases, receipt) and the two capability moments only.
    /// </summary>
    Narrated,

    /// <summary>
    /// Beat sentences come from the conservative neutral classification of tool calls.
    /// </summary>
    Classified,
}

/// <summary>
/// Per-projection options. <see cref="TidiedNarration"/> maps an agent-authored line's key
/// (see <see cref="AgentNarrationLine"/> — a progress note or a tool call's own description)
/// to its display form, and the map is deliberately three-state: a string replaces the raw
/// line, an explicit null <b>suppresses</b> it — the line carries nothing this reader wants
/// and earns no beat at all — and an absent key means no tidied form exists yet, so the raw
/// line shows. Suppression and unavailability must never collapse into one signal: a tidier
/// outage produces absent keys, so the feed degrades to raw wording rather than emptying.
/// Callers own tidying (and its caching) so the projector stays pure; a caller that reuses a
/// growing map across re-projections keeps delivered beats stable.
/// </summary>
public sealed record AgentActivityProjectionOptions
{
    public static readonly AgentActivityProjectionOptions Default = new();

    public AgentActivityBeatSource BeatSource { get; init; } = AgentActivityBeatSource.Auto;

    public IReadOnlyDictionary<string, string?>? TidiedNarration { get; init; }

    /// <summary>
    /// The consumer's own words for the steps and remarks it can name itself. A line it names
    /// never goes to the narration tidier, so naming is instant; a line it leaves alone follows
    /// the shared rules.
    /// </summary>
    public IAgentActivityStepLabeller? StepLabeller { get; init; }

    /// <summary>
    /// Shows the agent's text only between its tool calls. The text after the last call is the
    /// answer itself, which the consumer shows on its own.
    /// </summary>
    public bool RemarksBetweenToolCallsOnly { get; init; }
}

/// <summary>
/// One tool call as a step labeller sees it: the tool, the shell command for a Bash call, and the
/// file for a Read call. The labeller turns it into plain words and must never echo it.
/// </summary>
public sealed record AgentActivityToolCall(string ToolName, string? Command, string? FilePath);

/// <param name="Label">The plain-language line. Empty hides the step.</param>
/// <param name="Icon">A consumer-defined icon key the consumer's surface draws; null for none.</param>
public sealed record AgentActivityStep(string Label, string? Icon = null)
{
    public static AgentActivityStep Hidden { get; } = new(string.Empty);
}

/// <summary>
/// A consumer's deterministic naming of its agent's steps, supplied per projection. Each method
/// returns null to leave the line to the shared rules, a step to name it, or
/// <see cref="AgentActivityStep.Hidden"/> to hide it. Must be quick and must not throw.
/// </summary>
public interface IAgentActivityStepLabeller
{
    AgentActivityStep? LabelToolCall(AgentActivityToolCall call);

    /// <summary>The agent's own text between steps, as the reader should see it.</summary>
    AgentActivityStep? LabelRemark(string text);
}

/// <summary>
/// Which agent-authored channel a line came from. The two read differently — a note is
/// reflective prose about what was found, a tool action is an imperative about what is
/// about to be done — so a tidier serves each on its own terms rather than one register
/// pretending to fit both.
/// </summary>
public enum AgentActivityLineKind
{
    /// <summary>The agent's own progress note, written to itself.</summary>
    Narration = 0,

    /// <summary>The description the agent wrote for a tool call it is about to make.</summary>
    ToolAction,
}

/// <summary>
/// One agent-authored text line from the trace — a progress note, or the description the
/// agent wrote for one of its own tool calls — keyed stably by its trace record's uuid and
/// position so tidied forms can be cached across re-projections. The key is the cache
/// identity: it must depend only on the record, never on how much of the trace has been
/// read, or a live poll re-tidies every settled line on every pass.
/// </summary>
public sealed record AgentNarrationLine(string Key, DateTimeOffset At, string Text)
{
    public AgentActivityLineKind Kind { get; init; } = AgentActivityLineKind.Narration;
}

/// <summary>
/// Tidies the agent's own words — its progress notes and the descriptions it wrote for its
/// tool calls — for client display: one line in, one tidied line out, or an explicit null to
/// suppress a line that carries nothing this reader would want. Implementations receive only
/// those text lines and their <see cref="AgentNarrationLine.Kind"/>, never the trace, the
/// commands, or the tool inputs, so a tidied line structurally cannot leak one; they rewrite,
/// never summarise, and must not invent. The result is three-state and the states are not
/// interchangeable: a value rewrites, an explicit null suppresses, and a <b>missing</b> key
/// means "no tidied form — show the raw line". Never throws — a failed pass returns keys for
/// the lines it did answer and simply omits the rest, so the feed degrades to the agent's raw
/// words instead of emptying.
/// </summary>
public interface IAgentNarrationTidier
{
    Task<IReadOnlyDictionary<string, string?>> TidyAsync(
        IReadOnlyList<AgentNarrationLine> lines,
        CancellationToken cancellationToken = default);
}
