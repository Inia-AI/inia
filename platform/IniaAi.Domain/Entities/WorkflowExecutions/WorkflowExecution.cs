using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Counterparties;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.Entities.WorkflowExecutions;

[Index(nameof(IdempotencyKey), nameof(OrganizationId), IsUnique = true)]
[Index(nameof(HostBootId))]
public class WorkflowExecution : Entity<WorkflowExecution>
{
    public string? WorkflowId { get; set; }
    public string? WorkflowTypeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? OrganizationId { get; set; }
    public DateTime? ReviewDate { get; set; }
    public string? AnalystName { get; set; }
    public string? CounterpartyId { get; set; }
    public string? JobId { get; set; }
    public Dictionary<string, object>? Variables { get; set; }
    public bool IsBaseline { get; set; } = false;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowState WorkflowState { get; set; } = WorkflowState.Undefined;
    [MaxLength(IDEMPOTENCY_KEY_MAX_LENGTH)]
    public string? IdempotencyKey { get; set; }

    public const int IDEMPOTENCY_KEY_MAX_LENGTH = 64;
    public const int FAILURE_KIND_MAX_LENGTH = 64;
    public const int FAILURE_DETAIL_MAX_LENGTH = 2000;

    /// <summary>
    /// Why this execution ended <see cref="WorkflowState.Failed"/>, as one of
    /// <see cref="WorkflowExecutionFailureKinds"/> or a kind a workflow handler defines for its own
    /// workflow type. Null on runs that did not fail and on failures whose cause the engine could
    /// not classify.
    /// </summary>
    [MaxLength(FAILURE_KIND_MAX_LENGTH)]
    public string? FailureKind { get; set; }

    /// <summary>
    /// Human-readable explanation of the failure — which block failed and why — truncated to
    /// <see cref="FAILURE_DETAIL_MAX_LENGTH"/> when written.
    /// </summary>
    [MaxLength(FAILURE_DETAIL_MAX_LENGTH)]
    public string? FailureDetail { get; set; }

    /// <summary>
    /// Why this completed execution still needs attention, as one of
    /// <see cref="WorkflowExecutionWarningKinds"/> or a kind a workflow handler defines for its own
    /// workflow type. Null on runs that completed clean and on
    /// runs that did not complete. Set together with the completing state transition, never
    /// after — the warning is part of how the run concluded.
    /// </summary>
    [MaxLength(FAILURE_KIND_MAX_LENGTH)]
    public string? WarningKind { get; set; }

    /// <summary>
    /// Human-readable explanation of the warning — which verification group released
    /// unverified and why — truncated to <see cref="FAILURE_DETAIL_MAX_LENGTH"/> when written.
    /// </summary>
    [MaxLength(FAILURE_DETAIL_MAX_LENGTH)]
    public string? WarningDetail { get; set; }

    /// <summary>
    /// The template version (Core WorkflowVersion.Id) this execution ran against, stamped at
    /// execution time. Null when no version row existed for the org/type at run time. Cross-context
    /// pointer only, no navigation.
    /// </summary>
    [MaxLength(450)]
    public string? TemplateVersionId { get; set; }

    /// <summary>
    /// Content hash of the template this execution ran against, stamped at execution time. Used to
    /// derive baseline staleness (blessed hash vs live head hash) without re-running.
    /// </summary>
    [MaxLength(64)]
    public string? TemplateContentHash { get; set; }

    /// <summary>
    /// Serialized List&lt;ReplayUnmatched&gt; recorded during a replay run when a captured
    /// external output could not be safely mapped (0 or &gt;1 match) or a freeze-eligible block had
    /// no capture. On the dormant regression path a non-empty list marks the run inconclusive; on a
    /// freeze execution it names the blocks that ran live. Null for normal runs.
    /// </summary>
    public string? ReplayUnmatchedJson { get; set; }

    /// <summary>
    /// The completed execution whose external-fetch outputs this execution replayed (the freeze
    /// submission option). Non-null marks a frozen execution: a test artifact whose FX rates and
    /// search results are historical, never a normal report. Null for normal runs. When the
    /// replay was served from a case's stored captures (<see cref="FreezeSourceCaseId"/>), this
    /// still names the execution that produced them — an execution of the exporting deployment,
    /// so an id that does not resolve here.
    /// </summary>
    [MaxLength(450)]
    public string? FreezeSourceExecutionId { get; set; }

    /// <summary>
    /// The validation case whose stored freeze captures this execution replayed, when the freeze
    /// source was a case rather than a same-instance execution. Null otherwise.
    /// </summary>
    [MaxLength(450)]
    public string? FreezeSourceCaseId { get; set; }

    /// <summary>
    /// True when this execution replayed external fetches from any freeze source — an execution
    /// or a case's stored captures. Disclosure readers ask this, never a single column.
    /// </summary>
    [NotMapped]
    public bool IsFrozen => FreezeSourceExecutionId is not null || FreezeSourceCaseId is not null;

    /// <summary>
    /// How many freeze-eligible blocks of this replay run were served from a captured output
    /// instead of a live fetch. Null for normal runs.
    /// </summary>
    public int? FreezeReplayedCount { get; set; }

    /// <summary>
    /// How many freeze-eligible blocks this replay run executed (the coverage denominator). Null for
    /// normal runs.
    /// </summary>
    public int? FreezeEligibleCount { get; set; }

    /// <summary>
    /// UTC timestamp when the worker dequeued this execution and began processing
    /// (first state transition out of <see cref="WorkflowState.Queued"/>). Null while still queued.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// The process start (<see cref="HostBoot"/>) whose memory holds the run's job, so a run whose host
    /// stopped can be found; empty when none was recorded.
    /// </summary>
    [StringLength(450)]
    public string? HostBootId { get; set; }

    /// <summary>
    /// UTC timestamp when the execution reached a terminal state:
    /// <see cref="WorkflowState.Draft"/> (success awaiting review),
    /// <see cref="WorkflowState.Failed"/> (errored),
    /// <see cref="WorkflowState.Confirmed"/> (direct-confirm paths), or
    /// <see cref="WorkflowState.Cancelled"/> (user- or system-initiated cancel).
    /// Null until then.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Wall-clock duration from dequeue to terminal state. Null until both
    /// <see cref="StartedAt"/> and <see cref="CompletedAt"/> are set.
    /// </summary>
    [NotMapped]
    public TimeSpan? Duration => StartedAt is null || CompletedAt is null
        ? null
        : CompletedAt - StartedAt;

    /// <summary>
    /// True while the execution has not yet reached a terminal state, so it can
    /// still transition to <see cref="WorkflowState.Cancelled"/> or
    /// <see cref="WorkflowState.Failed"/>. Used by the cancel-state command
    /// handler, the JobExecutor's cancellation catches, and the isolated
    /// failed-state persistence so they all make the same decision in the same way.
    /// <see cref="WorkflowState.Undefined"/> counts as active: it is the creation default when
    /// no initial state is passed — every production caller passes one today, but existing rows
    /// hold it — and excluding it left those rows unable to reach any terminal state at all.
    /// </summary>
    public bool IsActive()
    {
        return IsActiveState(WorkflowState);
    }

    /// <summary>
    /// The states in which the execution has not yet reached a terminal outcome. The single source
    /// behind <see cref="IsActiveState"/> and the SQL-translatable
    /// <c>WorkflowExecutionExpressions.IsInActiveState</c>, so the in-memory guard and the
    /// conditional UPDATE's WHERE clause can never disagree.
    /// </summary>
    public static readonly WorkflowState[] ActiveStates =
    [
        WorkflowState.Running,
        WorkflowState.ProcessingDocuments,
        WorkflowState.Queued,
        WorkflowState.Undefined,
    ];

    /// <summary>
    /// <see cref="IsActive"/> for a bare state, so callers holding only the stored state — a
    /// block-boundary re-read, for instance — decide it the same way.
    /// </summary>
    public static bool IsActiveState(WorkflowState state)
    {
        return ActiveStates.Contains(state);
    }

    /// <summary>
    /// The states in which the execution finished and produced its report — draft (produced,
    /// awaiting review) or confirmed. The one definition of "completed" every consumer of an
    /// execution's report reads: baseline marking, regression re-runs, validation candidates.
    /// </summary>
    public static readonly WorkflowState[] CompletedStates =
    [
        WorkflowState.Draft,
        WorkflowState.Confirmed,
    ];

    /// <summary>
    /// Whether the execution finished and produced its report — see <see cref="CompletedStates"/>.
    /// </summary>
    public bool HasCompleted()
    {
        return CompletedStates.Contains(WorkflowState);
    }

    public ICollection<WorkflowExecutionCounterparty> WorkflowExecutionsCounterparties { get; set; } = [];
    public ICollection<WorkflowExecutionLog> Logs { get; set; } = [];
    public Counterparty? Counterparty { get; set; }
}
