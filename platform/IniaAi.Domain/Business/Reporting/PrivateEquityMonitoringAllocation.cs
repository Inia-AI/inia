using IniaAi.Domain.Common;
using IniaAi.Domain.Reruns;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.Business.Reporting;

[LiveData]
public class PrivateEquityMonitoringAllocation : Entity<PrivateEquityMonitoringAllocation>
{
    public string PrivateEquityMonitoringObservationId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Sector { get; set; }
    public string? Geography { get; set; }
    [Precision(18, 2)]
    public decimal? FairMarketValue { get; set; }
    [Precision(18, 2)]
    public decimal? Cost { get; set; }
    [Precision(18, 2)]
    public decimal? Weightage { get; set; }

    // Navigation
    public PrivateEquityMonitoringObservation? Observation { get; set; }
}
