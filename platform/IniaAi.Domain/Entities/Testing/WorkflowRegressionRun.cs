using System.ComponentModel.DataAnnotations;
using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;

namespace IniaAi.Domain.Entities.Testing;

public class WorkflowRegressionRun : Entity<WorkflowRegressionRun>, IOrganizationScoped, IIdentifiable
{
    // How long past TimeoutAt a run may sit at Comparing before it is considered stranded
    // (crashed mid-comparison): batch status reports it TimedOut and a re-compare is allowed.
    public const int COMPARING_TIMEOUT_GRACE_MINUTES = 30;


    public string OrganizationId { get; set; } = string.Empty;
    public string BaselineExecutionId { get; set; } = string.Empty;
    public string? RerunExecutionId { get; set; }
    public string? WorkflowTypeId { get; set; }
    public string? WorkflowTypeName { get; set; }
    public string? CounterpartyName { get; set; }
    // Concurrency token: the comparison handler claims a run by transitioning Running -> Comparing,
    // and the auto-trigger (JobExecutor) can race a manual compare call. The losing writer gets a
    // DbUpdateConcurrencyException instead of silently double-running the comparison.
    [ConcurrencyCheck]
    public WorkflowRegressionRunStatus Status { get; set; } = WorkflowRegressionRunStatus.Pending;
    public decimal OverallSimilarityPercent { get; set; }
    public int BlocksCompared { get; set; }
    public int BlocksMatched { get; set; }
    public int BlocksDiffered { get; set; }
    public int BlocksWithDefinitionChanges { get; set; }
    public int UnexpectedDifferences { get; set; }
    public int ExpectedDifferences { get; set; }
    public string? BatchId { get; set; }
    public int? TimeoutMinutes { get; set; }
    public DateTime? TimeoutAt { get; set; }
    public string TriggeredByUserId { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Informational version of the platform build that created this run (includes the commit
    /// SHA when stamped by CI). Captured at run creation; a deploy while the run is in flight
    /// may complete it on a newer build.
    /// </summary>
    [StringLength(100)]
    public string? PlatformVersion { get; set; }

    /// <summary>
    /// Why the run is <see cref="WorkflowRegressionRunStatus.Inconclusive"/> (which captured external
    /// outputs could not be safely mapped). Null unless the run is inconclusive.
    /// </summary>
    public string? InconclusiveReason { get; set; }

    public ICollection<WorkflowRegressionBlockResult> BlockResults { get; set; } = [];
}
