using IniaAi.Core.Workflows.Executions;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Services.Jobs;

public static class HostBootHeartbeatHosting
{
    /// <summary>
    /// Registers this process start's identity, which every execution row it creates records, with
    /// the heartbeat that keeps the start visible to the host-loss sweep. A host that runs the
    /// in-memory job queue registers both, so wherever host-loss recovery is on, every start a row
    /// names heartbeats.
    /// </summary>
    public static IServiceCollection AddHostBootHeartbeat(this IServiceCollection services)
    {
        return services
            .AddSingleton<HostBootIdentity>()
            .AddScoped<HostBootHeartbeat>()
            .AddHostedService<HostBootHeartbeatService>();
    }
}
