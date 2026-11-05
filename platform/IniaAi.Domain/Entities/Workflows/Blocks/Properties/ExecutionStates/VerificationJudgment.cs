using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// What one verification round concluded — the platform's record, not the model's. Exactly one
/// of <see cref="UncheckedReason"/> and <see cref="Verdict"/> is set: a judgment either carries
/// a real verdict derived from the per-criterion states, or names why the machinery could not
/// produce one (capacity pre-flight overflow, a broken group after skip gates, a failed or
/// malformed verifier leg). Machinery failure is not a verdict — the release surface maps it to
/// <c>unverified-unchecked</c>, never to a failure of the members' work. The advisory and smell
/// fields are instrumentation: recorded for the failure record and audit, never acted on.
/// </summary>
public class VerificationJudgment
{
    [JsonProperty(nameof(Round))]
    public int Round { get; set; } = 1;

    [JsonProperty(nameof(JudgedAtUtc))]
    public DateTime JudgedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Why no verdict exists, when none does. The reason lands verbatim on the release marker,
    /// so it is written for the analyst, not the log.
    /// </summary>
    [JsonProperty(nameof(UncheckedReason), NullValueHandling = NullValueHandling.Ignore)]
    public string? UncheckedReason { get; set; }

    [JsonProperty(nameof(Verdict), NullValueHandling = NullValueHandling.Ignore)]
    public VerificationVerdict? Verdict { get; set; }

    /// <summary>The model's own top-level outcome field, recorded and never acted on.</summary>
    [JsonProperty(nameof(AdvisoryOutcome), NullValueHandling = NullValueHandling.Ignore)]
    public string? AdvisoryOutcome { get; set; }

    /// <summary>
    /// A verdict that settled implausibly fast after a long member run is the fabrication failure
    /// class — near-instant runs carried fabricated answers. Recorded provenance only: it never
    /// blocks a release and never re-buys a pass.
    /// </summary>
    [JsonProperty(nameof(SuspiciouslyFast))]
    public bool SuspiciouslyFast { get; set; }

    [JsonProperty(nameof(SuspiciouslyCheap))]
    public bool SuspiciouslyCheap { get; set; }

    [JsonProperty(nameof(VerifierSeconds), NullValueHandling = NullValueHandling.Ignore)]
    public double? VerifierSeconds { get; set; }

    [JsonProperty(nameof(VerifierCostUsd))]
    public decimal VerifierCostUsd { get; set; }

    /// <summary>
    /// Normalization notes from parsing the envelope — collapsed duplicates, ignored
    /// out-of-scope entries. Instrumentation for the failure record, never a verdict input.
    /// </summary>
    [JsonProperty(nameof(ParseWarnings))]
    public List<string> ParseWarnings { get; set; } = [];

    public bool ShouldSerializeParseWarnings()
    {
        return ParseWarnings.Count > 0;
    }
}

/// <summary>
/// The platform-derived verdict of one verification round. <see cref="Outcome"/> and every
/// member's <see cref="VerificationMemberVerdict.DerivedVerdict"/> are computed from the
/// per-criterion states — any <c>unsatisfiable</c> fails the group on first assertion, else any
/// <c>not-met</c> is revisable with the implicated members as the redo set, else pass.
/// </summary>
public class VerificationVerdict
{
    [JsonProperty(nameof(Outcome))]
    public string Outcome { get; set; } = string.Empty;

    /// <summary>
    /// The union of implicated members across failing criteria — a per-member criterion
    /// implicates its member, a group criterion names its implicated members. Empty unless
    /// <see cref="Outcome"/> is fail-revisable.
    /// </summary>
    [JsonProperty(nameof(RedoMemberTags))]
    public List<string> RedoMemberTags { get; set; } = [];

    /// <summary>
    /// Assessed (met, not-met, unsatisfiable) vs abstained counts, derived for every verdict so
    /// a verifier quietly abstaining its way to a clean result is visible.
    /// </summary>
    [JsonProperty(nameof(AssessedCriteriaCount))]
    public int AssessedCriteriaCount { get; set; }

    [JsonProperty(nameof(CannotAssessCount))]
    public int CannotAssessCount { get; set; }

    [JsonProperty(nameof(Members))]
    public List<VerificationMemberVerdict> Members { get; set; } = [];

    [JsonProperty(nameof(GroupCriteria))]
    public List<VerificationCriterionVerdict> GroupCriteria { get; set; } = [];

    /// <summary>Every judged criterion entry — member-scope in member order, then group-scope.</summary>
    public IEnumerable<VerificationCriterionVerdict> AllCriteria()
    {
        return Members.SelectMany(static member => member.Criteria).Concat(GroupCriteria);
    }

    /// <summary>One member's own member-scope entries; group-scope entries never appear here.</summary>
    public IEnumerable<VerificationCriterionVerdict> CriteriaFor(string memberTag)
    {
        return Members
            .Where(member => string.Equals(member.MemberTag, memberTag, StringComparison.Ordinal))
            .SelectMany(static member => member.Criteria);
    }
}

public class VerificationMemberVerdict
{
    [JsonProperty(nameof(MemberTag))]
    public string MemberTag { get; set; } = string.Empty;

    /// <summary>
    /// Fail exactly when one of this member's own criteria, or a group criterion implicating
    /// it, failed. Derived — the model's word is <see cref="AdvisoryVerdict"/>.
    /// </summary>
    [JsonProperty(nameof(DerivedVerdict))]
    public string DerivedVerdict { get; set; } = string.Empty;

    [JsonProperty(nameof(AdvisoryVerdict), NullValueHandling = NullValueHandling.Ignore)]
    public string? AdvisoryVerdict { get; set; }

    [JsonProperty(nameof(Feedback), NullValueHandling = NullValueHandling.Ignore)]
    public string? Feedback { get; set; }

    [JsonProperty(nameof(Criteria))]
    public List<VerificationCriterionVerdict> Criteria { get; set; } = [];
}

public class VerificationCriterionVerdict
{
    [JsonProperty(nameof(CriterionId))]
    public string CriterionId { get; set; } = string.Empty;

    /// <summary>Copied from the declaration at judge time, so the record reads without it.</summary>
    [JsonProperty(nameof(Label))]
    public string Label { get; set; } = string.Empty;

    [JsonProperty(nameof(State))]
    public string State { get; set; } = string.Empty;

    [JsonProperty(nameof(Feedback))]
    public string Feedback { get; set; } = string.Empty;

    /// <summary>
    /// True when the platform wrote this entry because the verifier omitted the criterion —
    /// omission is never a pass, and the note doubles as the redo wrapper's feedback.
    /// </summary>
    [JsonProperty(nameof(PlatformGenerated))]
    public bool PlatformGenerated { get; set; }

    [JsonProperty(nameof(Evidence))]
    public List<VerificationEvidence> Evidence { get; set; } = [];

    /// <summary>Group-scope only: the members this finding names. Empty on member-scope entries.</summary>
    [JsonProperty(nameof(ImplicatedMembers))]
    public List<string> ImplicatedMembers { get; set; } = [];

    public bool ShouldSerializeImplicatedMembers()
    {
        return ImplicatedMembers.Count > 0;
    }
}

public class VerificationEvidence
{
    [JsonProperty(nameof(Kind))]
    public string Kind { get; set; } = string.Empty;

    [JsonProperty(nameof(Text))]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Quote spans only: whether the text is a verbatim substring of a staged file. A failed
    /// check flags the span and the criterion keeps its model-judged state — instrumentation,
    /// never a verdict flip. Null on computation evidence, which is stored typed and never
    /// re-executed.
    /// </summary>
    [JsonProperty(nameof(QuoteVerified), NullValueHandling = NullValueHandling.Ignore)]
    public bool? QuoteVerified { get; set; }
}
