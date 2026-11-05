using IniaAi.Domain.Common;
using IniaAi.Domain.Reruns;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace IniaAi.Domain.Business.Reporting;

[LiveData]
public class HedgeFundsMonitoringObservation : Entity<HedgeFundsMonitoringObservation>
{
    public string OrganizationId { get; set; } = string.Empty;
    public string? FundId { get; set; }
    public string? FundManagerId { get; set; }
    public string? FundManager { get; set; }
    public string? FundName { get; set; }
    public DateTime? ReportingDate { get; set; }
    [Precision(18, 4)]
    public decimal? PerformancePercent { get; set; }
    [Precision(18, 2)]
    public decimal? NetAssetValue { get; set; }
    public string? NavCurrency { get; set; }
    public string? JobId { get; set; }
    public string? Units { get; set; }
    public string? WarningMessage { get; set; }
    public string? ClearedWarningMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public string? TriggerEmailIdentifier { get; set; }
    public bool IsApproved { get; set; } = false;
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    public bool IsBaseline { get; set; } = false;
    public bool IsExternal { get; set; } = false;

    /// <summary>
    /// Sets the approval and records who gave it and when. An approval that already stands keeps
    /// its first record; removing the approval clears it.
    /// </summary>
    public void SetApproval(bool isApproved, string userId)
    {
        if (IsApproved == isApproved)
        {
            return;
        }

        IsApproved = isApproved;
        ApprovedAt = isApproved ? DateTime.UtcNow : null;
        ApprovedByUserId = isApproved ? userId : null;
    }

    public bool AcknowledgeWarning()
    {
        if (string.IsNullOrWhiteSpace(WarningMessage))
        {
            return false;
        }

        ClearedWarningMessage = WarningMessage;
        WarningMessage = null;
        return true;
    }

    public bool ReopenWarning()
    {
        if (string.IsNullOrWhiteSpace(ClearedWarningMessage) || !string.IsNullOrWhiteSpace(WarningMessage))
        {
            return false;
        }

        WarningMessage = ClearedWarningMessage;
        ClearedWarningMessage = null;
        return true;
    }

    /// <summary>
    /// Creates a shallow copy of the current HedgeFundsMonitoringObservation.
    /// The Id and audit fields are not copied.
    /// </summary>
    /// <returns></returns>
    public HedgeFundsMonitoringObservation Clone()
    {
        return new HedgeFundsMonitoringObservation
        {
            OrganizationId = OrganizationId,
            FundId = FundId,
            FundManagerId = FundManagerId,
            FundManager = FundManager,
            FundName = FundName,
            ReportingDate = ReportingDate,
            PerformancePercent = PerformancePercent,
            NetAssetValue = NetAssetValue,
            NavCurrency = NavCurrency,
            JobId = JobId,
            Units = Units,
            WarningMessage = WarningMessage,
            ClearedWarningMessage = ClearedWarningMessage,
            ErrorMessage = ErrorMessage,
            TriggerEmailIdentifier = TriggerEmailIdentifier,
            IsApproved = IsApproved,
            ApprovedAt = ApprovedAt,
            ApprovedByUserId = ApprovedByUserId,
            IsBaseline = IsBaseline,
            IsExternal = IsExternal
        };
    }

    public override string ToString()
    {
        var sb = new StringBuilder();

        _ = sb.AppendLine($"Fund Manager: {FundManager}");
        _ = sb.AppendLine($"Fund: {FundName}");
        _ = sb.AppendLine($"Reporting Date: {ReportingDate:yyyy-MM}");
        _ = sb.AppendLine($"Performance: {PerformancePercent:P2}");
        _ = sb.AppendLine($"NAV: {NetAssetValue} {NavCurrency}");
        _ = sb.AppendLine($"Units: {Units}");
        _ = sb.AppendLine($"Currency: {NavCurrency}");
        _ = sb.AppendLine($"Email Identifier: {TriggerEmailIdentifier}");

        if (!string.IsNullOrEmpty(WarningMessage))
        {
            _ = sb.AppendLine($"WarningMessage: {WarningMessage}");
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            _ = sb.AppendLine($"ErrorMessage: {ErrorMessage}");
        }

        return sb.ToString();
    }
}
