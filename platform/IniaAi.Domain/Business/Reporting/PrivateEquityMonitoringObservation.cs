using IniaAi.Domain.Common;
using IniaAi.Domain.Reruns;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.Business.Reporting;

[Index(nameof(OrganizationId), nameof(IsDeleted))]
[Index(nameof(FundId))]
[Index(nameof(JobId))]
[LiveData]
public class PrivateEquityMonitoringObservation : Entity<PrivateEquityMonitoringObservation>
{
    // Identity & metadata
    public string OrganizationId { get; set; } = string.Empty;
    public string? FundId { get; set; }
    public string? FundManagerId { get; set; }
    public string? FundManager { get; set; }
    public string? FundName { get; set; }
    public string? JobId { get; set; }

    // Capital Data
    public DateTime? ReportingDate { get; set; }
    [Precision(18, 2)]
    public decimal? NetAssetValue { get; set; }
    [Precision(18, 2)]
    public decimal? NetWorth { get; set; }
    [Precision(18, 4)]
    public decimal? GrossIrr { get; set; }
    [Precision(18, 4)]
    public decimal? NetIrr { get; set; }
    [Precision(18, 2)]
    public decimal? RecallableCapital { get; set; }
    [Precision(18, 2)]
    public decimal? UncalledCapital { get; set; }
    [Precision(18, 2)]
    public decimal? TotalCommitment { get; set; }
    [Precision(18, 2)]
    public decimal? Distributions { get; set; }
    [Precision(18, 2)]
    public decimal? TotalAssets { get; set; }
    [Precision(18, 2)]
    public decimal? TotalLiabilities { get; set; }
    public int? NumberOfInvestments { get; set; }
    public string? Currency { get; set; }

    // Fund Profile
    public string? FundStrategy { get; set; }
    public string? FundAssetGeography { get; set; }
    public string? FundAssetSector { get; set; }
    public string? Vintage { get; set; }
    public string? Term { get; set; }

    // Concentration analysis
    public string? ConcentrationSummary { get; set; }
    public string? ConcentrationFlags { get; set; }
    public string? DiversificationAssessment { get; set; }

    // Validation & status
    public string? WarningMessage { get; set; }
    public string? ClearedWarningMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsApproved { get; set; }
    public bool IsBaseline { get; set; }

    // Navigation
    public List<PrivateEquityMonitoringAllocation> Allocations { get; set; } = [];

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
}
