using IniaAi.Common;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Documents;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Screening;

/// <summary>
/// Where one backtest document lands. The first four count toward the agreement rate: each is a
/// document with one class the user chose that today's template still offers, and only
/// <see cref="Agreed"/> is a hit, because live screening would drop a document the judge could not
/// read or matched to nothing. The other three carry no one user class to check against, and
/// <see cref="DocumentGone"/> was deleted or lost from the file store before the backtest reached
/// it, so nothing was judged; all four are shown beside the rate, not in it.
/// </summary>
public enum ScreeningBacktestBucket
{
    Agreed = 0,
    Disagreed = 1,
    MatchedNothing = 2,
    Unreadable = 3,
    NoUserClass = 4,
    ClassNotInTemplate = 5,
    LabelConflict = 6,
    DocumentGone = 7,
}

/// <summary>
/// One input document of a backtest under one set of run variables: the class its users chose with
/// those variables, and what the judge said about it. Planned when the backtest starts, one row per
/// document id and run variables (<see cref="RunVariablesSha256"/>), newest run first; judged in
/// <see cref="Sequence"/> order and committed as each verdict arrives. A row whose bytes and run
/// variables match a row judged before it shares that verdict (<see cref="DuplicateOfId"/>) and
/// costs no call; the page counts such a file once. <see cref="Reasoning"/> and <see cref="EvidenceExcerpt"/> are the
/// only columns holding customer document content, and the retention sweep nulls both when the
/// document is hard-deleted. Carries no foreign key, like <see cref="ScreeningRecord"/>.
/// </summary>
[Index(nameof(BacktestId), nameof(Sequence))]
[Index(nameof(DocumentId))]
public class ScreeningBacktestDocument : Entity<ScreeningBacktestDocument>, IScreeningVerdictColumns
{
    public const int REASONING_MAX_LENGTH = 4000;
    public const int EVIDENCE_EXCERPT_MAX_LENGTH = ScreeningRecord.EVIDENCE_EXCERPT_MAX_LENGTH;
    public const int READ_FAILURE_MAX_LENGTH = 2000;

    [Required]
    [StringLength(450)]
    public string BacktestId { get; set; } = string.Empty;

    public int Sequence { get; set; }

    /// <summary>The organization of the newest run that sent the document with these variables; its model routing picks the judge's model.</summary>
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>The newest run that sent the document with these variables; the judge reads that run's variables.</summary>
    [Required]
    [StringLength(450)]
    public string WorkflowExecutionId { get; set; } = string.Empty;

    [Required]
    [StringLength(Document.MAX_NAME_LENGTH)]
    public string FileName
    {
        get;
        set => field = value.RemoveNullCharacters().Truncate(Document.MAX_NAME_LENGTH)!;
    } = string.Empty;

    /// <summary>
    /// Lowercase hex SHA-256 of the run variables the judge reads with this row, as
    /// <c>ScreeningRunVariables</c> renders them. The row holds the hash, never the values; the
    /// page reads the values from <see cref="WorkflowExecutionId"/>.
    /// </summary>
    [Required]
    [StringLength(64)]
    public string RunVariablesSha256 { get; set; } = string.Empty;

    /// <summary>Every class users sent this file under, as a JSON array; more than one makes a label conflict.</summary>
    public string UserClassesJson { get; set; } = "[]";

    public bool IsJudged { get; set; }

    /// <summary>The row judged earlier in this backtest whose bytes and run variables are the same; this row shares its verdict.</summary>
    [StringLength(450)]
    public string? DuplicateOfId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the judged bytes; with <see cref="RunVariablesSha256"/>, the key two backtests are compared on.</summary>
    [StringLength(64)]
    public string? ContentSha256 { get; set; }

    public bool? Readable { get; set; }

    /// <summary>Every offered class the judge matched, with its confidence, as <c>ScreeningMatchedClass</c> JSON.</summary>
    public string? MatchedClassesJson
    {
        get;
        set => field = value.RemoveNullCharacters();
    }

    /// <summary>The judge's highest-confidence match among the offered classes; a tie goes to the key first in ordinal order, as selection breaks it.</summary>
    [StringLength(450)]
    public string? BestMatch { get; set; }

    public ScreeningBacktestBucket? Bucket { get; set; }

    /// <summary>Why the platform could not read the file, when it could not; the document is then unreadable, as on a live run.</summary>
    [StringLength(READ_FAILURE_MAX_LENGTH)]
    public string? ReadFailure
    {
        get;
        set => field = value.RemoveNullCharacters().Truncate(READ_FAILURE_MAX_LENGTH);
    }

    /// <summary>The judge's own account of its verdict. Customer content: the retention sweep nulls it when the document is hard-deleted.</summary>
    [StringLength(REASONING_MAX_LENGTH)]
    public string? Reasoning
    {
        get;
        set => field = value.RemoveNullCharacters().Truncate(REASONING_MAX_LENGTH);
    }

    /// <summary>The cue the judge cited. Customer content: the retention sweep nulls it with the reasoning.</summary>
    [StringLength(EVIDENCE_EXCERPT_MAX_LENGTH)]
    public string? EvidenceExcerpt
    {
        get;
        set => field = value.RemoveNullCharacters().Truncate(EVIDENCE_EXCERPT_MAX_LENGTH);
    }

    /// <summary>Set when the retention sweep took the reasoning and the excerpt away.</summary>
    public DateTime? ContentClearedAt { get; set; }

    public bool PromptInjectionSuspected { get; set; }

    public bool MaliciousContentSuspected { get; set; }

    [StringLength(200)]
    public string? ModelId { get; set; }

    public DateOnly? PeriodEnd { get; set; }

    public int? PeriodMonths { get; set; }

    public DateTime? JudgedAt { get; set; }

    public IReadOnlyList<string> UserClasses()
    {
        return ScreeningJsonList.Read<string>(UserClassesJson);
    }
}
