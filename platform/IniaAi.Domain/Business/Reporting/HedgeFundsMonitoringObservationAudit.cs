using IniaAi.Domain.Common;
using IniaAi.Domain.Reruns;

namespace IniaAi.Domain.Business.Reporting;

[LiveData]
public class HedgeFundsMonitoringObservationAudit : Entity<HedgeFundsMonitoringObservationAudit>
{
    public string HedgeFundsMonitoringObservationId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation property
    public HedgeFundsMonitoringObservation? HedgeFundsMonitoringObservation { get; set; }
}