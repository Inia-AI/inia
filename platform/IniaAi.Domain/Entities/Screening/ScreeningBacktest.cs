using IniaAi.Common;
using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Screening;

public enum ScreeningBacktestStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>
/// One screening backtest: the input documents of a workflow type's past runs, judged again with
/// the type's classes, variable labels, screening guidance and screening prompts as they stood when
/// the backtest started, and compared with the class each user chose. The plan (its document and run rows) is
/// written at the start, the verdicts as each document finishes, so a backtest the host stopped
/// continues from its first unjudged document. It writes nothing to the runs it reads, and its
/// decisions never reach the screening statistics. Deliberately carries no foreign key, like
/// <see cref="ScreeningRecord"/>, so a measurement outlives the runs and documents it read.
/// </summary>
[Index(nameof(WorkflowTypeId), nameof(CreatedAt))]
[Index(nameof(Status))]
public class ScreeningBacktest : Entity<ScreeningBacktest>
{
    public const int DEFAULT_DOCUMENT_CAP = 300;
    public const int MAX_DOCUMENT_CAP = 5000;
    public const int FAILURE_MESSAGE_MAX_LENGTH = 2000;

    [Required]
    [StringLength(450)]
    public string WorkflowTypeId { get; set; } = string.Empty;

    /// <summary>The one organization whose runs were read; null when the backtest read every organization's runs.</summary>
    [StringLength(450)]
    public string? OrganizationId { get; set; }

    [Required]
    [StringLength(450)]
    public string StartedByUserId { get; set; } = string.Empty;

    /// <summary>The organization of the operator who started it; every judge call's spend lands on it.</summary>
    [Required]
    [StringLength(450)]
    public string StartedByOrganizationId { get; set; } = string.Empty;

    public ScreeningBacktestStatus Status { get; set; } = ScreeningBacktestStatus.Queued;

    public int DocumentCap { get; set; } = DEFAULT_DOCUMENT_CAP;

    public int PlannedDocumentCount { get; set; }

    public int PlannedRunCount { get; set; }

    /// <summary>How many document rows have a verdict, their own or one shared with the same bytes under the same run variables.</summary>
    public int JudgedDocumentCount { get; set; }

    /// <summary>How many files count toward the agreement rate, each file once for each set of run variables.</summary>
    public int ScoredCount { get; set; }

    /// <summary>How many of the scored files the judge agreed on.</summary>
    public int AgreedCount { get; set; }

    public int DecidedRunCount { get; set; }

    /// <summary>The Core template version the classes were read from; null when the type had no version row.</summary>
    [StringLength(450)]
    public string? TemplateVersionId { get; set; }

    [StringLength(64)]
    public string? TemplateContentHash { get; set; }

    /// <summary>
    /// The id of the prompts copied at the start, for tracing only: the stored row's id, or, when no
    /// row is stored, the fresh id the code defaults were given when they were read.
    /// </summary>
    [StringLength(450)]
    public string? PromptsRowId { get; set; }

    /// <summary>The judge's system prompt as it stood at the start; every judge call of the backtest uses this copy.</summary>
    public string? JudgeSystemPrompt { get; set; }

    /// <summary>The judge's prompt as it stood at the start; every judge call of the backtest uses this copy.</summary>
    public string? JudgePrompt { get; set; }

    /// <summary>The template's document classes as the judge was given them, in <c>ScreeningBacktestClass</c> form.</summary>
    public string ClassesJson { get; set; } = "[]";

    /// <summary>The template's variables as the judge reads them, in <c>ScreeningVariableLabel</c> form, frozen at the start.</summary>
    public string VariableLabelsJson { get; set; } = "[]";

    public string? Guidance { get; set; }

    [StringLength(FAILURE_MESSAGE_MAX_LENGTH)]
    public string? FailureMessage
    {
        get;
        set => field = value.Truncate(FAILURE_MESSAGE_MAX_LENGTH);
    }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>When an operator asked the backtest to stop. The runner stops before its next document.</summary>
    public DateTime? CancelRequestedAt { get; set; }

    /// <summary>
    /// How many times a runner took the backtest up. The concurrency token: a runner moves it when
    /// it starts, so a second runner on the same backtest, in this host or another, fails its next
    /// write and stops instead of judging the same documents twice.
    /// </summary>
    [ConcurrencyCheck]
    public int Attempt { get; set; }

    [NotMapped]
    public bool IsFinished => IsFinal(Status);

    public static bool IsFinal(ScreeningBacktestStatus status)
    {
        return status is ScreeningBacktestStatus.Completed or ScreeningBacktestStatus.Failed or ScreeningBacktestStatus.Cancelled;
    }

    /// <summary>Whether an operator can still cancel it: it has not finished, and no one asked it to stop yet.</summary>
    public static bool CanCancel(ScreeningBacktestStatus status, DateTime? cancelRequestedAt)
    {
        return !IsFinal(status) && cancelRequestedAt is null;
    }

    /// <summary>The prompts every judge call of the backtest uses: the copy taken at the start.</summary>
    public ScreeningPrompts Prompts()
    {
        return new ScreeningPrompts { JudgeSystemPrompt = JudgeSystemPrompt, JudgePrompt = JudgePrompt };
    }
}
