using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Services.Jobs;

/// <summary>
/// Heartbeats this process start from the moment the host starts, so the host-loss sweep can tell a
/// stopped process from a live one. Runs in every process that holds an in-memory job queue, and
/// only where <see cref="WorkflowExecutionJanitorSettings.HostLossRecovery"/> is on.
/// </summary>
public class HostBootHeartbeatService : PeriodicBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkflowExecutionJanitorSettings _settings;
    private readonly ILogger<HostBootHeartbeatService> _logger;

    public HostBootHeartbeatService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkflowExecutionJanitorSettings> settings,
        ILogger<HostBootHeartbeatService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override TimeSpan Interval => HostLossSweep.HEARTBEAT_INTERVAL;

    protected override bool ShouldStart()
    {
        if (!_settings.HostLossRecovery)
        {
            _logger.LogInformation("HostBootHeartbeat: host-loss recovery is off by configuration");
        }

        return _settings.HostLossRecovery;
    }

    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HostBootHeartbeat>().BeatAsync(cancellationToken);
    }

    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "HostBootHeartbeat: the heartbeat failed");
    }
}
