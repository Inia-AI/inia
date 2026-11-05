using IniaAi.Common;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Documents;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;

namespace IniaAi.Domain.Entities.Screening;

/// <summary>
/// Append-only record of one intake screening decision: a deterministic refusal at upload, or
/// the judge's verdict on one document inside one run. One row per decision, written as the
/// decision is made. Deliberately carries no foreign key so the trail survives document and
/// execution deletion; <see cref="Entity{T}.CreatedAt"/> is the decision timestamp.
/// <see cref="EvidenceExcerpt"/> and <see cref="Reasoning"/> are the only columns holding customer
/// document content, and the retention sweep nulls both when the document they were taken from is
/// hard-deleted; every other column is permanent.
/// Customer-derived text (<see cref="FileName"/>, <see cref="MatchedClassesJson"/>,
/// <see cref="EvidenceExcerpt"/>, <see cref="Reasoning"/>) is stripped of null characters and clipped to its column on
/// set, so no writer has to know the widths; identifiers and vocabularies are stored as given.
/// </summary>
[Index(nameof(OrganizationId), nameof(CreatedAt))]
[Index(nameof(CreatedAt))]
[Index(nameof(OrganizationId), nameof(WorkflowTypeId), nameof(CreatedAt))]
[Index(nameof(WorkflowExecutionId))]
[Index(nameof(DocumentId))]
[Index(nameof(OriginatingDocumentId))]
public class ScreeningRecord : Entity<ScreeningRecord>, IScreeningVerdictColumns
{
    public const int EVIDENCE_EXCERPT_MAX_LENGTH = 2000;
    public const int REASONING_MAX_LENGTH = 2000;

    private ScreeningRecord()
    {
    }

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>Null for a deterministic refusal at upload, which happens before any run exists.</summary>
    [StringLength(450)]
    public string? WorkflowExecutionId { get; set; }

    [StringLength(450)]
    public string? WorkflowTypeId { get; set; }

    /// <summary>The document whose bytes were screened. Null when the bytes were refused before a row existed.</summary>
    [StringLength(450)]
    public string? DocumentId { get; set; }

    /// <summary>
    /// The document the caller sent. Differs from <see cref="DocumentId"/> on a confidential run,
    /// where anonymisation repoints the run at a derivative and the judge screens that derivative.
    /// </summary>
    [StringLength(450)]
    public string? OriginatingDocumentId { get; set; }

    [Required]
    [StringLength(Document.MAX_NAME_LENGTH)]
    public string FileName
    {
        get;
        set => field = value.RemoveNullCharacters().Truncate(Document.MAX_NAME_LENGTH);
    } = string.Empty;

    /// <summary>
    /// Lowercase hex SHA-256 of the bytes that were actually screened, computed by <see cref="For"/>
    /// from those bytes. Null when the refusal was decided from the declared size before any byte
    /// was read (<see cref="ForUnread"/>).
    /// </summary>
    [StringLength(64)]
    public string? ContentSha256 { get; private set; }

    public long SizeBytes { get; private set; }

    [StringLength(100)]
    public string? DetectedType { get; set; }

    /// <summary>Every class the judge matched, each with its stated confidence, serialized as evidence.</summary>
    public string? MatchedClassesJson
    {
        get;
        set => field = value.RemoveNullCharacters();
    }

    /// <summary>The one class stamped on the document at selection, when it survived.</summary>
    [StringLength(450)]
    public string? AssignedClass { get; set; }

    /// <summary>One of <c>ScreeningDispositions</c>.</summary>
    [Required]
    [StringLength(32)]
    public string Disposition { get; set; } = string.Empty;

    /// <summary>One of <c>ScreeningReasonCodes</c> for a judge decision, or of <see cref="IntakeRefusalReasons"/> for a refusal at upload; null when the document was accepted.</summary>
    [StringLength(64)]
    public string? ReasonCode { get; set; }

    public bool PromptInjectionSuspected { get; set; }

    public bool MaliciousContentSuspected { get; set; }

    /// <summary>The cue the judge cited. Customer content: the retention sweep nulls it when the document is hard-deleted.</summary>
    [StringLength(EVIDENCE_EXCERPT_MAX_LENGTH)]
    public string? EvidenceExcerpt
    {
        get;
        set => field = value.RemoveNullCharacters().Truncate(EVIDENCE_EXCERPT_MAX_LENGTH);
    }

    /// <summary>The judge's own account of the cues behind its verdict. Customer content: the retention sweep nulls it with the excerpt.</summary>
    [StringLength(REASONING_MAX_LENGTH)]
    public string? Reasoning
    {
        get;
        set => field = value.RemoveNullCharacters().Truncate(REASONING_MAX_LENGTH);
    }

    /// <summary>
    /// Set when the retention sweep nulls <see cref="EvidenceExcerpt"/>. It is the sole signal that
    /// an excerpt was taken away: a null excerpt alone cannot be told apart from a verdict that never
    /// cited one, so a flag-rate review must check this timestamp, not the excerpt, to know which.
    /// </summary>
    public DateTime? ExcerptClearedAt { get; set; }

    [StringLength(200)]
    public string? ModelId { get; set; }

    public DateOnly? PeriodEnd { get; set; }

    public int? PeriodMonths { get; set; }

    /// <summary>The screening prompts row the verdict was produced under, so a flag-rate review can tell prompt versions apart.</summary>
    [StringLength(450)]
    public string? PromptsRowId { get; set; }

    [NotMapped]
    public bool IsFlagged => PromptInjectionSuspected || MaliciousContentSuspected;

    public static ScreeningRecord For(
        string organizationId,
        string fileName,
        ReadOnlySpan<byte> screenedBytes,
        string disposition)
    {
        ScreeningRecord record = ForUnread(organizationId, fileName, screenedBytes.Length, disposition);
        record.ContentSha256 = Convert.ToHexStringLower(SHA256.HashData(screenedBytes));
        return record;
    }

    public static ScreeningRecord ForUnread(
        string organizationId,
        string fileName,
        long sizeBytes,
        string disposition)
    {
        return new ScreeningRecord
        {
            OrganizationId = organizationId,
            FileName = fileName,
            SizeBytes = sizeBytes,
            Disposition = disposition,
        };
    }
}
