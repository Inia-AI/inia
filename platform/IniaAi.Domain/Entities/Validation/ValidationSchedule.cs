using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// One suite's enrollment in the schedule. Enrollment carries how often the suite runs and, for a
/// cadence that names one, on which weekday — the hour inside that day stays deployment-wide
/// policy. The target id is loose like every other id in this model: a deleted suite or a
/// retired workflow type leaves a row that resolves to nothing and simply runs no cases.
/// <para>
/// The row also carries what the last attempt did, because an act that starts nothing leaves no
/// runs behind to read — a refused attempt would otherwise be invisible outside the log.
/// </para>
/// </summary>
[Index(nameof(OrganizationId), nameof(TargetKind), nameof(TargetId), IsUnique = true)]
public class ValidationSchedule : Entity<ValidationSchedule>, IOrganizationScoped, IIdentifiable
{
    public const int MAX_FAILURE_MESSAGE_LENGTH = 4000;

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    public ValidationScheduleTargetKind TargetKind { get; set; }

    [Required]
    [StringLength(450)]
    public string TargetId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the schedule runs this suite. Kept as a flag rather than by deleting the row, so
    /// a suite switched off and on again keeps <see cref="LastRunAt"/> and does not pay for a
    /// second act on a day it has already run.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the schedule takes the act: every day, every week or every two weeks.
    /// </summary>
    public ValidationScheduleCadence Cadence { get; set; } = ValidationScheduleCadence.Daily;

    /// <summary>
    /// The weekday a weekly or fortnightly schedule runs on; null for a daily one. A weekly or
    /// fortnightly row without one is never due, so the validator refuses to save that shape.
    /// </summary>
    public DayOfWeek? Weekday { get; set; }

    /// <summary>
    /// Who enrolled it. Every scheduled act runs as this person: the runs carry the id, the
    /// executions are submitted under it, and the token spend is billed to it — enrollment is
    /// the act of authorizing that spend.
    /// </summary>
    [Required]
    [StringLength(450)]
    public string EnrolledByUserId { get; set; } = string.Empty;

    /// <summary>
    /// When the schedule last started an act for this suite. What makes a day's act happen once:
    /// a sweep in today's window skips a suite that already ran in it.
    /// </summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>The act the last successful attempt created, so its runs are reachable from here.</summary>
    [StringLength(450)]
    public string? LastBatchId { get; set; }

    /// <summary>
    /// When the schedule last tried and started nothing, and why. Both are cleared by the next
    /// attempt that starts an act, so what the workspace shows is the current state and never a
    /// failure the schedule has since recovered from.
    /// </summary>
    public DateTime? LastFailureAt { get; set; }

    [StringLength(MAX_FAILURE_MESSAGE_LENGTH)]
    public string? LastFailureMessage { get; set; }
}
