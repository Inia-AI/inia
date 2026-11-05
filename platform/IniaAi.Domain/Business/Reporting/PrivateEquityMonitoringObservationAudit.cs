using IniaAi.Domain.Common;
using IniaAi.Domain.Reruns;

namespace IniaAi.Domain.Business.Reporting;

[LiveData]
public class PrivateEquityMonitoringObservationAudit : Entity<PrivateEquityMonitoringObservationAudit>
{
    public string PrivateEquityMonitoringObservationId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation property
    public PrivateEquityMonitoringObservation? PrivateEquityMonitoringObservation { get; set; }
}
