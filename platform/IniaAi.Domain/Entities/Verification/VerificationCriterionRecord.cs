using IniaAi.Common;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Verification;

/// <summary>
/// One criterion verdict from one verification pass — the append-only longitudinal record
/// workflows improve on (spec §11, verification-agent-v1.md in the agent-runtime repo).
/// Written for every criterion on every pass, passes included: a failure count without a
/// denominator proves nothing. Group-scope criteria write one row per round with the member
/// columns null. Deliberately relationship-free: every id is a loose indexed string, because an
/// FK to the block would cascade the record away with its execution, and a navigation-derived
/// tenant inherits the measured 38.8% NULL organization rate on executed workflow rows. Rows
/// outlive their execution; hard-delete cleanup nulls only <see cref="Feedback"/> and
/// <see cref="EvidenceSpans"/> — text follows the customer, aggregates are permanent.
/// </summary>
[Index(nameof(OrganizationId), nameof(JudgedAtUtc))]
[Index(nameof(OrganizationId), nameof(WorkflowTypeId), nameof(CriterionId), nameof(JudgedAtUtc))]
[Index(nameof(OrganizationId), nameof(TemplateVersionId), nameof(JudgedAtUtc))]
[Index(nameof(WorkflowExecutionId))]
[Index(nameof(RunExecutionId))]
public class VerificationCriterionRecord
{
    public const int ADVISORY_MAX_LENGTH = 64;

    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Direct tenant anchor, stamped by the writer — never derived through a navigation.</summary>
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string WorkflowExecutionId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string WorkflowTypeId { get; set; } = string.Empty;

    /// <summary>Core WorkflowVersion.Id the execution ran against; null when no version row existed.</summary>
    [StringLength(450)]
    public string? TemplateVersionId { get; set; }

    [StringLength(64)]
    public string? TemplateContentHash { get; set; }

    /// <summary>The verification block's stable id — the identity that survives template saves.</summary>
    [Required]
    [StringLength(450)]
    public string VerifierBlockId { get; set; } = string.Empty;

    /// <summary>The judged member's stable block id. Null on group-scope rows.</summary>
    [StringLength(450)]
    public string? MemberBlockId { get; set; }

    /// <summary>The judged member's replacement tag. Null on group-scope rows.</summary>
    [StringLength(450)]
    public string? MemberTag { get; set; }

    public int Round { get; set; }

    /// <summary>
    /// The run id of the verifier pass that produced this verdict, for joining the row to its
    /// ledger entry and trace files — and the key that keeps a crash-replayed persist idempotent.
    /// </summary>
    [Required]
    [StringLength(128)]
    public string RunExecutionId { get; set; } = string.Empty;

    [Required]
    [StringLength(VerificationCriterion.ID_MAX_LENGTH)]
    public string CriterionId { get; set; } = string.Empty;

    /// <summary>The human label as declared at dispatch, so the row reads without the template.</summary>
    [Required]
    public string CriterionLabel
    {
        get;
        set => field = value.RemoveNullCharacters();
    } = string.Empty;

    /// <summary>
    /// SHA-256 of the criterion's normalized definition exactly as dispatched, excluding
    /// presentation order. Without it, a failure rate that fell because someone softened the
    /// rubric is indistinguishable from a workflow that improved.
    /// </summary>
    [Required]
    [StringLength(64)]
    public string CriterionDefinitionHash { get; set; } = string.Empty;

    /// <summary>The four-state per-criterion verdict: met, not-met, cannot-assess, unsatisfiable.</summary>
    [Required]
    [StringLength(32)]
    public string Met { get; set; } = string.Empty;

    /// <summary>The model's own top-level outcome for the round — instrumentation, never acted on.</summary>
    [StringLength(ADVISORY_MAX_LENGTH)]
    public string? AdvisoryOutcome { get; set; }

    /// <summary>The model's own verdict for the judged member. Null on group-scope rows.</summary>
    [StringLength(ADVISORY_MAX_LENGTH)]
    public string? AdvisoryMemberVerdict { get; set; }

    /// <summary>The member's declared redo mode at dispatch. Null on group-scope rows.</summary>
    [StringLength(32)]
    public string? ConfiguredRedoMode { get; set; }

    /// <summary>The redo mode a dispatched redo actually ran with. Null until a redo runs.</summary>
    [StringLength(32)]
    public string? ActualRedoMode { get; set; }

    public int EvidenceSpanCount { get; set; }

    /// <summary>Serialized evidence spans. Nulled when the source execution is hard-deleted.</summary>
    public string? EvidenceSpans
    {
        get;
        set => field = value?.RemoveNullCharacters();
    }

    /// <summary>The verifier's feedback text. Nulled when the source execution is hard-deleted.</summary>
    public string? Feedback
    {
        get;
        set => field = value?.RemoveNullCharacters();
    }

    /// <summary>
    /// What the round's verifier legs cost in total (every attempt, not just the settled pass),
    /// stamped on every row of the round — so summing this column across a round's rows
    /// multiplies by the criterion count rather than totalling spend. Verifier-scoped, never
    /// whole-round: the members' redo legs are not in it. The round's real total is the sum of
    /// the run-pass ledgers of the verifier and of every member block.
    /// </summary>
    [Precision(18, 6)]
    public decimal EstimatedCostUsd { get; set; }

    public DateTime JudgedAtUtc { get; set; }
}
