using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// The provenance vocabulary of <see cref="VerificationReleaseMarker.Provenance"/>. v1 makes no
/// affirmative "verified" claim to the analyst: the report surfaces flagged criteria and the
/// unverified markers only, so <see cref="VERIFIED"/> renders as the absence of a warning, not
/// as a badge.
/// </summary>
public static class VerificationProvenance
{
    /// <summary>No criterion failed; cannot-assess entries ride the marker with their reasons.</summary>
    public const string VERIFIED = "verified";

    /// <summary>Pass cap spent with criteria still failing; the marker names them by label.</summary>
    public const string UNVERIFIED_EXHAUSTED = "unverified-exhausted";

    /// <summary>The verifier machinery could not run; the marker carries the reason.</summary>
    public const string UNVERIFIED_UNCHECKED = "unverified-unchecked";
}

/// <summary>
/// What verification actually happened to the released work. Derivable from the round's
/// <see cref="VerificationJudgment"/> today, but stamped and persisted deliberately: the
/// judgment-to-provenance mapping is release policy that changes over time (a failed round maps
/// to exhausted only until the redo loop exists), so the marker records the decision made under
/// the rules of the day rather than re-deriving it under later rules. The marker is the
/// analyst-facing record; the judgment stays the machine one.
/// </summary>
public class VerificationReleaseMarker
{
    /// <summary>One of <see cref="VerificationProvenance"/>.</summary>
    [JsonProperty(nameof(Provenance))]
    public string Provenance { get; set; } = string.Empty;

    /// <summary>
    /// Unchecked only: why the machinery could not judge, written for the analyst. A later round
    /// that could not judge is not this case — an earlier round's verdict carries the release, so
    /// the failure is why the loop stopped and rides <see cref="StopReason"/>.
    /// </summary>
    [JsonProperty(nameof(UncheckedReason), NullValueHandling = NullValueHandling.Ignore)]
    public string? UncheckedReason { get; set; }

    /// <summary>
    /// Exhausted only: the still-failing criteria by their human labels, the reason the work
    /// releases unverified.
    /// </summary>
    [JsonProperty(nameof(FailedCriteria))]
    public List<VerificationMarkerNote> FailedCriteria { get; set; } = [];

    /// <summary>
    /// Criteria the verifier abstained on, with their stated reasons. These surface on every
    /// provenance value rather than blocking a verified stamp.
    /// </summary>
    [JsonProperty(nameof(CannotAssessCriteria))]
    public List<VerificationMarkerNote> CannotAssessCriteria { get; set; } = [];

    /// <summary>
    /// Set when the released set mixes attempts that were never group-checked together — a
    /// member's released attempt is not the one the final verify pass judged. The statement
    /// says what was not checked together; it is never a verified badge, and it renders even
    /// under a verified stamp with no abstentions.
    /// </summary>
    [JsonProperty(nameof(MixedRoundStatement), NullValueHandling = NullValueHandling.Ignore)]
    public string? MixedRoundStatement { get; set; }

    /// <summary>
    /// Exhausted-by-no-progress only: the criterion IDs the final round failed identically to
    /// the round before it — the reason the loop stopped buying rounds early.
    /// </summary>
    [JsonProperty(nameof(NoProgressCriterionIds))]
    public List<string> NoProgressCriterionIds { get; set; } = [];

    /// <summary>
    /// Exhausted only: why the loop stopped without a passing verdict, in one field because the
    /// analyst needs one answer. Two things produce it — the loop declined its next repair round
    /// because the round's worst case no longer fit the execution's lifetime window, or it bought
    /// the round and the round could not be judged. Distinct from <see cref="UncheckedReason"/>,
    /// which belongs to a release no verdict ever judged.
    /// </summary>
    [JsonProperty(nameof(StopReason), NullValueHandling = NullValueHandling.Ignore)]
    public string? StopReason { get; set; }

    /// <summary>
    /// Reads a marker written while this field was called <c>WindowStopReason</c>, so a rename
    /// costs no stored reason. Read-only on purpose: it never serializes, so the old name dies
    /// out as markers are rewritten. Both serializers reach a persisted marker — the executed
    /// workflow's JSON column through System.Text.Json, the workflow file through Newtonsoft —
    /// so the alias answers to both. Delete it once no stored marker can carry the old name.
    /// </summary>
    [JsonProperty("WindowStopReason", NullValueHandling = NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonPropertyName("WindowStopReason")]
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyWindowStopReason
    {
        get => null;
        set => StopReason ??= value;
    }

    public bool ShouldSerializeLegacyWindowStopReason()
    {
        return false;
    }

    public bool ShouldSerializeNoProgressCriterionIds()
    {
        return NoProgressCriterionIds.Count > 0;
    }

    public bool ShouldSerializeFailedCriteria()
    {
        return FailedCriteria.Count > 0;
    }

    public bool ShouldSerializeCannotAssessCriteria()
    {
        return CannotAssessCriteria.Count > 0;
    }

    public bool IsUnverified()
    {
        return Provenance is VerificationProvenance.UNVERIFIED_EXHAUSTED
            or VerificationProvenance.UNVERIFIED_UNCHECKED;
    }

    /// <summary>
    /// Whether the marker renders anything to the analyst. A clean verified release renders
    /// nothing — v1 makes no affirmative "verified" claim — so only unverified provenance,
    /// abstentions, or a mixed-round release put a note on the report.
    /// </summary>
    public bool HasAnalystNote()
    {
        return IsUnverified()
            || CannotAssessCriteria.Count > 0
            || !string.IsNullOrWhiteSpace(MixedRoundStatement);
    }
}

/// <summary>One criterion on the marker: its human label and the analyst-facing note.</summary>
public class VerificationMarkerNote
{
    [JsonProperty(nameof(Label))]
    public string Label { get; set; } = string.Empty;

    [JsonProperty(nameof(Note))]
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// The round whose verdict this note comes from: a member-scope note carries the round its
    /// member released from, a group-scope note the final round (D63). Null on markers written
    /// before rounds were attributed.
    /// </summary>
    [JsonProperty(nameof(Round), NullValueHandling = NullValueHandling.Ignore)]
    public int? Round { get; set; }
}
