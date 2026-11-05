using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// One agent run an Agent block bought, kept on the executed block so a failure names what it
/// cost and which runs to look at. The runtime holds a settled run's trace pack for its
/// retention window, so an executionId here is still an address, not just a label. A row is
/// written the moment its run starts — id, pass, and the unacknowledged marker, flushed before
/// the first poll — and enriched when the run settles, so a process death mid-run still leaves
/// a row that names the burning run.
/// </summary>
public class AgentRunPass
{
    /// <summary>
    /// Which verification leg bought this run, counting from one. Rows written before the
    /// round axis existed deserialize as round one — the only round that existed then.
    /// </summary>
    [JsonProperty(nameof(Round))]
    public int Round { get; set; } = 1;

    /// <summary>Which pass this run was, counting from one.</summary>
    [JsonProperty(nameof(Pass))]
    public int Pass { get; set; }

    /// <summary>The run id the runtime knows it by.</summary>
    [JsonProperty(nameof(ExecutionId))]
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// The run's last observed status, so a killed-mid-flight run and one that finished free
    /// stay distinguishable in the record. Absent on a start-time row whose run has not
    /// settled yet, and on rows written before the field existed.
    /// </summary>
    [JsonProperty(nameof(Status), NullValueHandling = NullValueHandling.Ignore)]
    public string? Status { get; set; }

    /// <summary>The runtime's failure subtype, absent unless the run settled failed.</summary>
    [JsonProperty(nameof(FailureSubtype), NullValueHandling = NullValueHandling.Ignore)]
    public string? FailureSubtype { get; set; }

    /// <summary>
    /// True while the platform has not acknowledged this run on the runtime — written true at
    /// start and only ever cleared, which is why the start-time flush alone keeps the record
    /// safe. A row still carrying the marker names a run that terminal-transition cleanup and
    /// the janitor must complete via the quiet verbs. False on rows written before the marker
    /// existed: those runs settled under the old lifecycle, and enumerating them would send
    /// completes years after the fact.
    /// </summary>
    [JsonProperty(nameof(Unacknowledged))]
    public bool Unacknowledged { get; set; }

    /// <summary>What the runtime measured this run spending.</summary>
    [JsonProperty(nameof(EstimatedCostUsd))]
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>
    /// How long the run took by the runtime's own monitor clock. Absent on a row whose run never
    /// settled — a failed attempt carries no monitor of its own, so its duration is unknown
    /// rather than zero — and on rows written before the field existed.
    /// </summary>
    [JsonProperty(nameof(DurationSeconds), NullValueHandling = NullValueHandling.Ignore)]
    public double? DurationSeconds { get; set; }

    /// <summary>
    /// The fabrication smell signals for this leg: a run that came back faster or cheaper than a
    /// real one plausibly could. Recorded provenance, never a block and never a re-buy — the
    /// group's spend panel and the loop's burst check read them, and a whole round of instant
    /// zero-cost failures is an infrastructure signal, not a verdict about the work.
    /// </summary>
    [JsonProperty(nameof(SuspiciouslyFast))]
    public bool SuspiciouslyFast { get; set; }

    /// <inheritdoc cref="SuspiciouslyFast"/>
    [JsonProperty(nameof(SuspiciouslyCheap))]
    public bool SuspiciouslyCheap { get; set; }

    /// <summary>
    /// The redo mode the prompt engineer configured for the member this leg redid, present only
    /// on a verification redo leg (D36f, D37i). This pair is the named home for per-round mode
    /// and attribution bookkeeping: a later per-round attribution field lands beside these two,
    /// never on a new surface. Run bookkeeping only — the analytics copy rides
    /// <c>VerificationCriterionRecord</c>. Queried from jsonb with camelCase keys
    /// (<c>configuredRedoMode</c>): the persisted properties blob is written by System.Text.Json
    /// under a camelCase naming policy that ignores these Newtonsoft attributes.
    /// </summary>
    [JsonProperty(nameof(ConfiguredRedoMode), NullValueHandling = NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfiguredRedoMode { get; set; }

    /// <summary>
    /// The mode the leg actually ran with. Differs from <see cref="ConfiguredRedoMode"/> when a
    /// configured Revise degraded to scratch — a recorded override, never a failure (D36e).
    /// </summary>
    [JsonProperty(nameof(ActualRedoMode), NullValueHandling = NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ActualRedoMode { get; set; }

    /// <summary>
    /// True when best-attempt retention rejected this run's attempt in favor of an earlier
    /// round's (D14): the run settled, but its work does not stand as the member's current
    /// deliverable, so current-work reads skip it. Per-round attribution bookkeeping, landing
    /// beside the mode stamps per this type's own rule. False on every row written before the
    /// field existed — those runs predate the retention loop.
    /// </summary>
    [JsonProperty(nameof(Superseded))]
    public bool Superseded { get; set; }

    public bool ShouldSerializeSuperseded()
    {
        return Superseded;
    }

    public bool ShouldSerializeSuspiciouslyFast()
    {
        return SuspiciouslyFast;
    }

    public bool ShouldSerializeSuspiciouslyCheap()
    {
        return SuspiciouslyCheap;
    }
}
