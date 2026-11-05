using IniaAi.Domain.Common;
using IniaAi.Domain.Reruns;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Business.Reporting;

/// <summary>
/// One threshold control run on one hedge fund observation: what it measured, against which
/// threshold, and whether it fired. It is saved when the control runs, so a later report reads
/// the threshold that was in force then, not today's.
/// </summary>
[LiveData]
public class HedgeFundsControlResult : Entity<HedgeFundsControlResult>
{
    public const int CONTROL_MAX_LENGTH = 64;
    public const int OUTCOME_MAX_LENGTH = 16;

    // A NAV stored without a unit can reach the observation column's full range, and a change
    // measured against a tiny NAV grows past it; a value that does not fit loses the whole save.
    private const int VALUE_PRECISION = 28;
    private const int VALUE_SCALE = 6;

    public string OrganizationId { get; set; } = string.Empty;

    public string HedgeFundsMonitoringObservationId { get; set; } = string.Empty;

    [ForeignKey(nameof(HedgeFundsMonitoringObservationId))]
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public HedgeFundsMonitoringObservation? HedgeFundsMonitoringObservation { get; set; }

    [MaxLength(CONTROL_MAX_LENGTH)]
    public string Control { get; set; } = string.Empty;

    /// <summary>Fired, Passed, or NotRun when the observation had nothing to compare with.</summary>
    [MaxLength(OUTCOME_MAX_LENGTH)]
    public string Outcome { get; set; } = string.Empty;

    /// <summary>The observation's own value: its performance as a fraction, or its NAV.</summary>
    [Precision(VALUE_PRECISION, VALUE_SCALE)]
    public decimal? CurrentValue { get; set; }

    /// <summary>The earlier observation's value the control compared with.</summary>
    [Precision(VALUE_PRECISION, VALUE_SCALE)]
    public decimal? ComparedValue { get; set; }

    /// <summary>The number the control held against the threshold, as a fraction.</summary>
    [Precision(VALUE_PRECISION, VALUE_SCALE)]
    public decimal? MeasuredValue { get; set; }

    /// <summary>The threshold in force when the control ran, as a fraction.</summary>
    [Precision(18, 6)]
    public decimal? Threshold { get; set; }

    public string? CurrentCurrency { get; set; }

    public string? ComparedCurrency { get; set; }

    public string? ComparedObservationId { get; set; }

    public DateTime? ComparedReportingDate { get; set; }

    /// <summary>The warning sentence the control wrote when it fired.</summary>
    public string? Message { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
