using System.Text.Json.Nodes;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Full start payload for <see cref="IAgentRuntimeClient.StartRunAsync(AgentRunStartRequest, System.Threading.CancellationToken)"/>.
/// Documents and skill files share the runtime's decoded byte budget and its staged-input name
/// rules; the client validates both before any HTTP is attempted, so an over-budget or
/// ill-named input fails loudly instead of burning an upload. <paramref name="RunToken"/> is
/// scoped to a single run: a multi-pass execution re-mints the id per pass but carries this
/// token verbatim, so a caller that mints per-run tokens must keep
/// <see cref="AgentRunExecutionOptions.MaxPasses"/> at one.
/// </summary>
public sealed record AgentRunStartRequest(
    string ExecutionId,
    string Prompt,
    string? RunToken)
{
    /// <summary>Input documents staged into the run's document store.</summary>
    public IReadOnlyList<AgentRunDocument> Documents { get; init; } = [];

    /// <summary>Named skill files bundled into the worker's skill directory.</summary>
    public IReadOnlyList<AgentRunSkillFile> SkillFiles { get; init; } = [];

    /// <summary>
    /// Model for the run. Must be on the runtime's allowlist — a disallowed value is rejected
    /// by the runtime with 400 before the run owns any state.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Reasoning effort for the run (wire <c>effort</c>). Null omits the field, leaving the
    /// runtime's own default. A value outside the runtime's vocabulary is rejected with 400
    /// before the run owns any state.
    /// </summary>
    public string? Effort { get; init; }

    /// <summary>
    /// Optional system prompt for the worker (wire <c>systemPrompt</c>). Must be non-blank
    /// when set — the runtime rejects empty strings.
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// Optional JSON Schema the run's final output must conform to, sent as the wire's
    /// <c>outputFormat</c> with type <c>json_schema</c>. The runtime caps the serialized
    /// schema at 256 KiB; the client pre-flights the same limit.
    /// </summary>
    public JsonObject? OutputSchema { get; init; }

    /// <summary>
    /// MCP server URL handed to the worker. Unused until the MCP batch; carried so the wire
    /// shape does not change when it lands.
    /// </summary>
    public string? McpServerUrl { get; init; }

    /// <summary>
    /// Wall-clock fuse for the run (wire <c>deadlineMs</c>). Null omits the field, leaving the
    /// runtime's own default in force. A fuse may only tighten the runtime's ceiling: a larger
    /// value is refused with 400 before any spend, never clamped. Wide on purpose: the value is
    /// composed from author-facing minutes, and an over-ceiling product must survive the trip
    /// intact for the runtime to refuse it — an int would wrap it into a value the runtime
    /// happily accepts.
    /// </summary>
    public long? DeadlineMs { get; init; }

    /// <summary>
    /// Spend fuse for the run (wire <c>maxBudgetUsd</c>), same omit-and-tighten-only contract as
    /// <see cref="DeadlineMs"/>. The only fuse that accepts a fractional value.
    /// </summary>
    public decimal? MaxBudgetUsd { get; init; }

    /// <summary>
    /// Turn fuse for the run (wire <c>maxTurns</c>), same omit-and-tighten-only contract as
    /// <see cref="DeadlineMs"/>.
    /// </summary>
    public int? MaxTurns { get; init; }

    /// <summary>
    /// Retained-output fuse for the run (wire <c>maxRetainedOutputBytes</c>), same
    /// omit-and-tighten-only contract as <see cref="DeadlineMs"/>. The runtime charges this at
    /// admission rather than at publish, so it is what a run costs the pool while it runs.
    /// </summary>
    public long? MaxRetainedOutputBytes { get; init; }

    /// <summary>
    /// Resumes a finished session instead of starting a fresh one (wire
    /// <c>continuation</c>). <see cref="Prompt"/> is then the revision turn, delivered as the resumed
    /// session's answer; every fuse is still declared fresh per leg. Null starts from scratch.
    /// </summary>
    public AgentRunContinuation? Continuation { get; init; }

    /// <summary>
    /// The runtime admission class the run is admitted under (wire <c>admissionClass</c>). Null
    /// omits the field and the runtime admits the run as a workflow run. A runtime older than the
    /// agent-runtime release with its ADR 0142 rejects the field with 400, so callers set it
    /// only behind <c>AgentRuntimeSettings.SendAdmissionClass</c>.
    /// </summary>
    public AgentRunAdmissionClass? AdmissionClass { get; init; }
}

/// <summary>
/// A continuation start's cargo: the platform-held session bundle and the resumed run's own
/// re-carried <c>out/</c> files. The runtime verifies every declared digest before any spend
/// and re-stages nothing of its own, so this set is the whole workspace the resumed worker
/// wakes to. Re-carried file bytes charge the shared input byte budget
/// (<see cref="AgentRunInputCapacity.INPUT_BUDGET_BYTES"/>); the bundle does not.
/// </summary>
public sealed record AgentRunContinuation(
    ReadOnlyMemory<byte> SessionBundle,
    string SessionBundleSha256,
    IReadOnlyList<AgentRunRecarriedFile> OutFiles);

/// <summary>
/// One re-carried <c>out/</c> file; bytes are base64-encoded on the wire. <paramref name="Path"/>
/// is out/-relative ("checks/model.py", never "out/checks/model.py") and path-shaped by
/// contract — its own staged area with its own rules (<see cref="AgentRunContinuationCapacity"/>),
/// a separate namespace from documents and skill files, so it is never flattened through
/// <see cref="AgentStagedNames"/>.
/// </summary>
public sealed record AgentRunRecarriedFile(string Path, ReadOnlyMemory<byte> Content, string Sha256);

/// <summary>An input document riding the start request; bytes are base64-encoded on the wire.</summary>
public sealed record AgentRunDocument(
    string Name,
    ReadOnlyMemory<byte> Content,
    string? MediaType = null,
    string? DocumentClass = null);

/// <summary>
/// A skill for the worker's bundle; UTF-8 text, base64-encoded on the wire. <paramref name="Name"/>
/// is the skill's invocation identity (see <see cref="AgentSkillNames"/>), and
/// <paramref name="Description"/> is what the runtime writes into the skill's frontmatter for the
/// SDK to register — omitted only for templates saved before skills carried one.
/// </summary>
public sealed record AgentRunSkillFile(string Name, string Content, string? Description = null)
{
    /// <summary>
    /// Auxiliary files sent beside the skill (wire <c>skillFiles[].assets</c>). The runtime
    /// stages them read-only where the run's sandboxed Bash can read and execute them,
    /// addressed as <c>$SKILL_ASSETS_DIR/&lt;skill-name&gt;/&lt;asset-name&gt;</c> (ADR 0116 in
    /// agent-runtime). Empty means the wire carries no <c>assets</c> field at all.
    /// </summary>
    public IReadOnlyList<AgentRunSkillAsset> Assets { get; init; } = [];
}

/// <summary>
/// An auxiliary file riding a skill; UTF-8 text, base64-encoded on the wire. Asset names obey
/// the runtime's staged-input name rule and are unique within their skill — validated by the
/// client before any HTTP, mirroring the runtime's 400.
/// </summary>
public sealed record AgentRunSkillAsset(string Name, string Content);
