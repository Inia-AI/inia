using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// One validation run: a fully self-contained result document comparing a candidate against
/// its case's cached reference. Snapshots (resolved schema, reference-as-compared, candidate
/// extraction) are copied at run creation, so a later cache correction or schema edit never
/// changes what an in-flight or historical run measured against. The <see cref="Case"/>
/// foreign key is deliberately the only real FK in the validation model; candidate identity
/// (<see cref="CandidateSourceKind"/> + <see cref="CandidateSourceId"/>) is loose so run
/// history survives execution and document purges.
/// </summary>
[Index(nameof(CaseId), nameof(CreatedAt))]
[Index(nameof(OrganizationId), nameof(BatchId))]
[Index(nameof(OrganizationId), nameof(SuiteId), nameof(CreatedAt), nameof(BatchId))]
[Index(nameof(OrganizationId), nameof(ActTargetKind), nameof(ActTargetId), nameof(CreatedAt), nameof(BatchId))]
[Index(nameof(OrganizationId), nameof(CreatedAt))]
[Index(nameof(Status), nameof(CreatedAt))]
[Index(nameof(OrganizationId), nameof(CandidateSourceId))]
public class ValidationRun : Entity<ValidationRun>, IOrganizationScoped, IIdentifiable
{
    public const int MAX_FAILURE_MESSAGE_LENGTH = 4000;

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string CaseId { get; set; } = string.Empty;

    [ForeignKey(nameof(CaseId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public ValidationCase? Case { get; set; }

    public ValidationRunStatus Status { get; set; } = ValidationRunStatus.Pending;

    /// <summary>
    /// The scored outcome, null until scoring completes. Separate from <see cref="Status"/>
    /// on purpose: lifecycle and verdict are different questions, and needs-review and
    /// nothing-to-score are first-class queryable outcomes, never a vacuous pass.
    /// </summary>
    public ValidationRunOutcome? Outcome { get; set; }

    public byte[]? ResolvedSchemaJsonCompressed { get; set; }

    [NotMapped]
    public string ResolvedSchemaJson
    {
        get => WorkflowJsonCompression.Decompress(ResolvedSchemaJsonCompressed);
        set => ResolvedSchemaJsonCompressed = WorkflowJsonCompression.Compress(value);
    }

    /// <summary>
    /// The reference exactly as compared, copied at run creation from the case's cache.
    /// </summary>
    public byte[]? ReferenceSnapshotJsonCompressed { get; set; }

    [NotMapped]
    public string ReferenceSnapshotJson
    {
        get => WorkflowJsonCompression.Decompress(ReferenceSnapshotJsonCompressed);
        set => ReferenceSnapshotJsonCompressed = WorkflowJsonCompression.Compress(value);
    }

    /// <summary>
    /// The candidate extraction with its grounding quotes. Committed before judging begins,
    /// so a failed or partial run resumes from the persisted extraction instead of paying
    /// for it again.
    /// </summary>
    public byte[]? CandidateExtractionJsonCompressed { get; set; }

    [NotMapped]
    public string CandidateExtractionJson
    {
        get => WorkflowJsonCompression.Decompress(CandidateExtractionJsonCompressed);
        set => CandidateExtractionJsonCompressed = WorkflowJsonCompression.Compress(value);
    }

    /// <summary>
    /// The candidate text as compared — the flattened execution report or the pasted text —
    /// so the run is a self-contained result document whose quotes can be checked long after
    /// the source execution is purged, and so a failed run re-extracts without its source.
    /// </summary>
    public byte[]? CandidateTextCompressed { get; set; }

    [NotMapped]
    public string CandidateText
    {
        get => WorkflowJsonCompression.Decompress(CandidateTextCompressed);
        set => CandidateTextCompressed = WorkflowJsonCompression.Compress(value);
    }

    public int CandidateTextLength { get; set; }

    [StringLength(64)]
    public string? CandidateTextHash { get; set; }

    /// <summary>
    /// The full verdict table (<c>ValidationComparisonResult</c>): every field's both values,
    /// quotes, verdict, judge justification, and the comparison warnings. Written after the
    /// deterministic comparison and rewritten as judge verdicts land, so a resumed run picks
    /// up the fields still awaiting.
    /// </summary>
    public string? VerdictsJson { get; set; }

    /// <summary>
    /// The scorecard (<c>ValidationScorecard</c>) as computed at completion: group subscores,
    /// critical failures, and warnings beneath the headline scalars stored beside it.
    /// </summary>
    public string? ScorecardJson { get; set; }

    /// <summary>
    /// What the run had to leave out or change, as a JSON list of sentences: chart paragraphs
    /// the candidate flattening skipped, a resume that ran under a changed prompt set, rows
    /// and groups the double-read held or could not verify. Stated on the run rather than
    /// silent.
    /// </summary>
    public string? NotesJson { get; set; }

    /// <summary>
    /// The run's input-document record (<c>ValidationRunInputs</c>): what the case's recipe
    /// declared and what the candidate execution actually consumed, snapshotted at pairing so
    /// the inputs-differed statement survives the execution's purge and a later recipe edit.
    /// Null for text candidates and for runs predating the capture.
    /// </summary>
    public string? InputDocumentsJson { get; set; }

    /// <summary>
    /// Whether the consumed inputs differed from the declared set, derived from the record at
    /// pairing and frozen with it — list surfaces read this scalar instead of the payload.
    /// Null wherever the record is null, which reads as unknown, never as a change.
    /// </summary>
    public bool? InputsChangedSinceBaseline { get; set; }

    /// <summary>
    /// Why the last attempt stopped; null once an attempt completes. A stalled run — in flight
    /// past its deadline — carries no message of its own: that state is derived at read time.
    /// </summary>
    [StringLength(MAX_FAILURE_MESSAGE_LENGTH)]
    public string? FailureMessage { get; set; }

    /// <summary>
    /// Per-field adjudication overlay (<c>ValidationAdjudication</c>): extraction-error
    /// exclusions and reviewer-stated verdicts. An overlay so the run's original verdicts stay
    /// immutable; the scorecard shows both rates.
    /// </summary>
    public string? AdjudicationJson { get; set; }

    public double? MatchRatePercent { get; set; }

    /// <summary>
    /// The scorecard's rate with every gated critical field counted twice — what workflow
    /// health aggregates. Null on runs completed before the weighted rate existed; health
    /// falls back to <see cref="MatchRatePercent"/> there.
    /// </summary>
    public double? WeightedRatePercent { get; set; }

    public int ScoreableFieldCount { get; set; }

    public int MatchedFieldCount { get; set; }

    /// <summary>
    /// The scorecard's count of values the run could not settle, as it reads once the overlay
    /// is applied. The retention sweep waits while it is above zero, so a passing run keeps
    /// the report it read until every such value is settled.
    /// </summary>
    public int UnsettledFieldCount { get; set; }

    /// <summary>
    /// The headline as it reads once the overlay is applied; null while no reviewer has marked
    /// anything. Denormalized beside the original because list surfaces read a projection and
    /// never a payload column — the full adjudicated scorecard is recomputed on detail reads.
    /// </summary>
    public double? AdjudicatedMatchRatePercent { get; set; }

    public double? AdjudicatedWeightedRatePercent { get; set; }

    public ValidationRunOutcome? AdjudicatedOutcome { get; set; }

    public int? AdjudicatedScoreableFieldCount { get; set; }

    public int? AdjudicatedMatchedFieldCount { get; set; }

    /// <summary>
    /// How many judged prose fields a reviewer overrode. The judge-health signal the workspace
    /// counts across a case's recent runs, kept as a scalar for the same projection reason.
    /// </summary>
    public int JudgeOverrideCount { get; set; }

    public int? FreezeReplayedCount { get; set; }

    public int? FreezeEligibleCount { get; set; }

    public long TotalInputTokens { get; set; }

    public long TotalOutputTokens { get; set; }

    [Precision(18, 10)]
    public decimal TotalCost { get; set; }

    public ValidationCandidateSourceKind CandidateSourceKind { get; set; }

    [StringLength(450)]
    public string? CandidateSourceId { get; set; }

    [StringLength(450)]
    public string? CandidateWorkflowTypeId { get; set; }

    public DateTime? CandidateExecutedAt { get; set; }

    /// <summary>
    /// The template version the case's baseline execution ran under, copied from the case at
    /// run creation: run summaries project from the run row alone, and the execution the
    /// case read it from is deletable.
    /// </summary>
    [StringLength(450)]
    public string? BaselineTemplateVersionId { get; set; }

    [StringLength(64)]
    public string? BaselineTemplateContentHash { get; set; }

    /// <summary>
    /// The template version the candidate execution ran under, copied from the execution's
    /// stamp at pairing; null for pasted-text candidates and pre-stamping executions.
    /// </summary>
    [StringLength(450)]
    public string? CandidateTemplateVersionId { get; set; }

    /// <summary>
    /// Compared against <see cref="BaselineTemplateContentHash"/> to state whether the
    /// workflow changed under the run — both present and unequal means it did; either
    /// missing reads as unknown, never as a change.
    /// </summary>
    [StringLength(64)]
    public string? CandidateTemplateContentHash { get; set; }

    [StringLength(200)]
    public string? ExtractorModelId { get; set; }

    [StringLength(200)]
    public string? JudgeModelId { get; set; }

    /// <summary>
    /// Content hash of the prompt set actually sent — the prompt-set pattern has no stampable
    /// version, so this is what makes a harness step change attributable by inspection.
    /// </summary>
    [StringLength(64)]
    public string? PromptSetHash { get; set; }

    [StringLength(64)]
    public string? ExtractionFingerprint { get; set; }

    public int ReferenceRevisionUsed { get; set; }

    /// <summary>
    /// The act that created this run. Every run of one suite act shares it, and progress is a
    /// count of those rows by status — the act has no entity of its own.
    /// </summary>
    [StringLength(450)]
    public string? BatchId { get; set; }

    /// <summary>
    /// Which of the case's configured repeats this run is, from zero. With
    /// <see cref="BatchId"/> and <see cref="CaseId"/> it names the execution the act submits,
    /// so a resumed act finds the execution it already submitted instead of starting a second.
    /// </summary>
    public int RepeatIndex { get; set; }

    /// <summary>
    /// The suite the act ran, when it ran one; null for an act over every active case. Loose by
    /// the model's rule — a deleted suite leaves its runs readable.
    /// </summary>
    [StringLength(450)]
    public string? SuiteId { get; set; }

    /// <summary>
    /// What the act was taken over. <see cref="SuiteId"/> answers only for a curated suite, so
    /// an act over a workflow's automatic suite, over named cases, or over every active case had
    /// nothing to be listed under until this. Runs written before the column carry
    /// <see cref="ValidationActTargetKind.Unknown"/>.
    /// </summary>
    public ValidationActTargetKind ActTargetKind { get; set; }

    /// <summary>
    /// What the act's target named, when its kind names anything: a suite id or a workflow type
    /// id. Loose by the model's rule, and null for the targets that name no single thing.
    /// </summary>
    [StringLength(450)]
    public string? ActTargetId { get; set; }

    /// <summary>
    /// Who took the act. The advancer submits this run's execution as that person and hands the
    /// run to the queue as them, long after the request that started the act has gone. On a
    /// scheduled act this is whoever enrolled the suite, so the spend has an owner either way.
    /// </summary>
    [StringLength(450)]
    public string? TriggeredByUserId { get; set; }

    /// <summary>
    /// Whether a person or the daily schedule took the act. Read together with
    /// <see cref="TriggeredByUserId"/>: a quiet week of green scheduled runs and a week nobody
    /// ran anything are otherwise the same picture.
    /// </summary>
    public ValidationRunTrigger Trigger { get; set; }

    /// <summary>
    /// How many times the run was submitted or resumed. The concurrency token: a resume moves
    /// it, so an attempt still in flight when the run was resubmitted (a stalled attempt that
    /// was merely slow) fails its next write and is discarded instead of writing over the newer
    /// attempt.
    /// </summary>
    [ConcurrencyCheck]
    public int Attempt { get; set; }

    public DateTime? SubmittedAt { get; set; }

    /// <summary>
    /// A non-terminal run past this deadline reads and re-runs as failed — stalled is derived
    /// at read time; the platform has no background reaper.
    /// </summary>
    public DateTime? DeadlineAt { get; set; }

    /// <summary>
    /// The run this case is measured against. At most one per case: accepting clears the
    /// siblings, so the anchor a compare view reads is never ambiguous.
    /// </summary>
    public bool Accepted { get; set; }

    public DateTime? AcceptedAt { get; set; }

    [StringLength(450)]
    public string? AcceptedByUserId { get; set; }

    /// <summary>
    /// Set when retention discards the run's evidence (<c>ValidationRunRetention</c>). A run
    /// discarded by an earlier trim also lost its payload columns, so a reader that needs one
    /// checks the column, never this timestamp, to know whether it is there.
    /// </summary>
    public DateTime? PayloadTrimmedAt { get; set; }

    /// <summary>The one reading of the trim stamp every refusal asks.</summary>
    [NotMapped]
    public bool PayloadTrimmed => PayloadTrimmedAt is not null;
}
