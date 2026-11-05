using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Services.Jobs;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// Periodic shell around <see cref="HostLossSweep"/>: at startup and every minute, where
/// <see cref="WorkflowExecutionJanitorSettings.HostLossRecovery"/> is on. One instance in WebApi
/// covers the deployment; the guarded terminal write keeps a redundant sweep harmless.
/// </summary>
public class HostLossSweepService : PeriodicBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkflowExecutionJanitorSettings _settings;
    private readonly ILogger<HostLossSweepService> _logger;

    /// <summary>Takes the scope factory each sweep resolves the sweep from.</summary>
    public HostLossSweepService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkflowExecutionJanitorSettings> settings,
        ILogger<HostLossSweepService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    /// <inheritdoc/>
    protected override bool ShouldStart()
    {
        if (!_settings.HostLossRecovery)
        {
            _logger.LogInformation("HostLossSweep: host-loss recovery is off by configuration");
        }

        return _settings.HostLossRecovery;
    }

    /// <inheritdoc/>
    protected override void OnStarting()
    {
        _logger.LogInformation(
            "HostLossSweep: starting - sweep every {Interval}, a host is lost {LostAfter} after its last heartbeat",
            Interval,
            HostLossSweep.LOST_AFTER);
    }

    /// <inheritdoc/>
    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "HostLossSweep: unhandled error in sweep cycle");
    }

    /// <inheritdoc/>
    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        int marked = await scope.ServiceProvider.GetRequiredService<HostLossSweep>().SweepOnceAsync(cancellationToken);
        if (marked > 0)
        {
            _logger.LogInformation("HostLossSweep: failed {Count} run(s) of a stopped host", marked);
        }
    }
}
