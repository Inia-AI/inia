using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// A validation case: one baseline document plus the selection of schema fields it must
/// reproduce, and the cached human-reviewed reference extraction every run compares against.
/// The case owns snapshots of everything it depends on — the baseline text and the resolved
/// schema are copied onto the case, and <see cref="SchemaId"/> / <see cref="BaselineDocumentId"/> /
/// <see cref="BaselineExecutionId"/> are loose ids resolved at the application layer — so
/// reprocessing, refactoring, or purging the sources never silently invalidates or bricks it.
/// UI removal archives (<see cref="ValidationCaseState.Archived"/>), never deletes.
/// </summary>
[Index(nameof(OrganizationId), nameof(State))]
[Index(nameof(OrganizationId), nameof(SourceInstance), nameof(SourceCaseId))]
[Index(nameof(SchemaId))]
[Index(nameof(BaselineExecutionId))]
public class ValidationCase : Entity<ValidationCase>, IOrganizationScoped, IIdentifiable
{
    public const int MAX_NAME_LENGTH = 250;

    public const int MAX_EXTRACTION_ERROR_LENGTH = 4000;

    public const int MAX_SOURCE_INSTANCE_LENGTH = 200;

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(MAX_NAME_LENGTH)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether <see cref="Name"/> is the platform's own derivation rather than words a person
    /// typed. Only a derived name may be re-qualified when a sibling widens the report's schema
    /// span; a name a person wrote is never rewritten. Stored rather than inferred because the
    /// derivation reads a live counterparty name, so re-deriving it later can differ from what
    /// was stored and would disguise a derived name as a typed one.
    /// </summary>
    public bool NameIsDerived { get; set; }

    [Required]
    [StringLength(450)]
    public string SchemaId { get; set; } = string.Empty;

    /// <summary>
    /// Source document of the baseline text, when one exists; null for pasted text.
    /// Rendered as "source document removed" when the id no longer resolves.
    /// </summary>
    [StringLength(450)]
    public string? BaselineDocumentId { get; set; }

    /// <summary>
    /// The execution whose flattened report became the baseline, when the baseline is
    /// platform-produced. Provenance plus the replay source for output-freeze runs.
    /// </summary>
    [StringLength(450)]
    public string? BaselineExecutionId { get; set; }

    /// <summary>
    /// The template version the baseline execution ran under, copied from the execution's
    /// stamp at case creation — the execution itself is deletable while the case lives.
    /// Null for text and document baselines, and for executions predating the stamping.
    /// </summary>
    [StringLength(450)]
    public string? BaselineTemplateVersionId { get; set; }

    /// <summary>
    /// Content hash of the template the baseline execution ran under, copied like
    /// <see cref="BaselineTemplateVersionId"/>. The hash, not the version id, is what a run
    /// compares against its candidate's — a re-save with identical content is not a change.
    /// </summary>
    [StringLength(64)]
    public string? BaselineTemplateContentHash { get; set; }

    public byte[]? BaselineTextCompressed { get; set; }

    [NotMapped]
    public string BaselineText
    {
        get => WorkflowJsonCompression.Decompress(BaselineTextCompressed);
        set => BaselineTextCompressed = WorkflowJsonCompression.Compress(value);
    }

    public int BaselineTextLength { get; set; }

    /// <summary>
    /// Hash of the uncompressed baseline text, computed at snapshot time — never derived
    /// from stored payload bytes, so it stays provider-independent.
    /// </summary>
    [StringLength(64)]
    public string? BaselineTextHash { get; set; }

    /// <summary>
    /// What the baseline flattening could not carry (chart paragraphs skipped, for an
    /// execution baseline), as a JSON array of strings; null when there is nothing to say.
    /// </summary>
    public string? BaselineNotesJson { get; set; }

    /// <summary>
    /// Snapshot of the schema as resolved for this case, taken at activation and refreshed
    /// on re-extraction. Staleness is a local compare against the live schema's fingerprint.
    /// </summary>
    public byte[]? ResolvedSchemaJsonCompressed { get; set; }

    [NotMapped]
    public string ResolvedSchemaJson
    {
        get => WorkflowJsonCompression.Decompress(ResolvedSchemaJsonCompressed);
        set => ResolvedSchemaJsonCompressed = WorkflowJsonCompression.Compress(value);
    }

    public string? SelectedFieldsJson { get; set; }

    /// <summary>
    /// The re-run recipe (workflow type, input document refs, variables, freeze default,
    /// repeat count) for platform-baseline cases; null for document-only, compare-only cases.
    /// </summary>
    public string? RerunRecipeJson { get; set; }

    /// <summary>
    /// The workflow type the re-run recipe submits against — the recipe's own value lifted
    /// into a column so list surfaces filter and group by it without reading the recipe.
    /// Null for compare-only cases.
    /// </summary>
    [StringLength(450)]
    public string? WorkflowTypeId { get; set; }

    /// <summary>
    /// The cached reference extraction: current values, source quotes, and per-field
    /// confirmed/corrected state. Committed in one write at extraction end — a partial
    /// reference is never persisted.
    /// </summary>
    public string? ReferenceJson { get; set; }

    /// <summary>
    /// Fingerprint of the extractor-visible schema subset for this case's selected fields.
    /// Tolerances, criticality, and volatile flags sit outside it, so tolerance tuning
    /// never invalidates the cached reference.
    /// </summary>
    [StringLength(64)]
    public string? ExtractionFingerprint { get; set; }

    [StringLength(200)]
    public string? ReferenceModelId { get; set; }

    public DateTime? ReferenceExtractedAt { get; set; }

    /// <summary>
    /// When the extraction the case is currently in was started. There is no background
    /// reaper: a case sitting in <see cref="ValidationCaseState.Extracting"/> long past this
    /// is stuck, and the remedy is a user-triggered retry.
    /// </summary>
    public DateTime? ReferenceExtractionStartedAt { get; set; }

    /// <summary>
    /// Why the last extraction attempt failed; cleared by the next successful one. Lets a
    /// stuck case say so instead of looking merely slow.
    /// </summary>
    [StringLength(MAX_EXTRACTION_ERROR_LENGTH)]
    public string? ReferenceExtractionError { get; set; }

    /// <summary>
    /// Incremented by every state write — starting an extraction, its commit, every review
    /// write, archiving; each run records the revision it compared against. The concurrency
    /// check makes a stale write — a second reviewer from the same view, or an extraction still
    /// in flight when the case was restarted or archived — fail instead of silently overwriting.
    /// </summary>
    [ConcurrencyCheck]
    public int ReferenceRevision { get; set; }

    /// <summary>
    /// The number of reference values awaiting or holding a confirmation — one per selected
    /// scalar and one per selected member of every extracted row — so a group contributes a
    /// value per row, not per definition.
    /// </summary>
    public int SelectedFieldCount { get; set; }

    public int ConfirmedFieldCount { get; set; }

    /// <summary>
    /// What reading the baseline has cost so far, summed over every extraction attempt the case
    /// has paid for. A candidate extraction reads a document of the same shape against the same
    /// schema, so this is what a first run of this case is estimated from — before the case has
    /// any run history to estimate from instead.
    /// </summary>
    public int ReferenceInputTokens { get; set; }

    public int ReferenceOutputTokens { get; set; }

    [Precision(18, 10)]
    public decimal ReferenceCost { get; set; }

    public ValidationCaseState State { get; set; } = ValidationCaseState.Draft;

    /// <summary>
    /// The deployment a case bundle was exported from, for a case that arrived by import;
    /// null for a native case. With <see cref="SourceCaseId"/> this is the cross-instance
    /// identity a re-import supersedes on.
    /// </summary>
    [StringLength(MAX_SOURCE_INSTANCE_LENGTH)]
    public string? SourceInstance { get; set; }

    /// <summary>
    /// The case's id on the instance that exported it; null for a native case.
    /// </summary>
    [StringLength(450)]
    public string? SourceCaseId { get; set; }

    /// <summary>
    /// The freeze captures of the source baseline execution, carried in by import so a frozen
    /// run can replay external fetches without that execution existing on this deployment.
    /// A serialized list of replay outputs; null when the bundle carried none.
    /// </summary>
    public byte[]? FreezeCapturesJsonCompressed { get; set; }

    [NotMapped]
    public string? FreezeCapturesJson
    {
        get => FreezeCapturesJsonCompressed is null ? null : WorkflowJsonCompression.Decompress(FreezeCapturesJsonCompressed);
        set => FreezeCapturesJsonCompressed = value is null ? null : WorkflowJsonCompression.Compress(value);
    }

    /// <summary>
    /// True when the case carries stored freeze captures. The one place "has captures" is
    /// decided; reads the raw column so the test never inflates the blob.
    /// </summary>
    [NotMapped]
    public bool HasStoredFreezeCaptures => FreezeCapturesJsonCompressed is not null;

    public ICollection<ValidationRun> Runs { get; set; } = [];
}

/// <summary>
/// Case lifecycle. Only an <see cref="Active"/> case can run; a fingerprint change drops an
/// active case back to <see cref="ReferenceInReview"/> after re-extraction. A case stuck in
/// <see cref="Extracting"/> offers a user-triggered retry — the platform has no background reaper.
/// </summary>
public enum ValidationCaseState
{
    Draft = 0,
    Extracting = 1,
    ReferenceInReview = 2,
    Active = 3,
    Archived = 4,
}
