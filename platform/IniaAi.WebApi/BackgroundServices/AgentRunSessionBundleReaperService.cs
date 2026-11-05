using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using IniaAi.Services.Jobs;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// Periodic shell around <see cref="IAgentRunSessionBundleReaper.SweepExpiredAsync"/> — the
/// backstop that collects session bundles no execution terminal ever released (see
/// <see cref="AgentRunSessionBundleReaperSettings"/> for the policy). One instance in WebApi
/// covers the deployment; a redundant sweep deletes nothing the first one left.
/// </summary>
public class AgentRunSessionBundleReaperService : PeriodicBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentRunSessionBundleReaperSettings _settings;
    private readonly ILogger<AgentRunSessionBundleReaperService> _logger;

    /// <summary>Takes the scope factory each sweep cycle resolves the reaper from.</summary>
    public AgentRunSessionBundleReaperService(
        IServiceScopeFactory scopeFactory,
        IOptions<AgentRunSessionBundleReaperSettings> settings,
        ILogger<AgentRunSessionBundleReaperService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, _settings.SweepIntervalMinutes));

    /// <summary>
    /// Offset from the stuck-execution janitor's own delay so the two sweeps do not open their
    /// first database read on the same tick of a cold start.
    /// </summary>
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(3);

    /// <inheritdoc/>
    protected override bool ShouldStart()
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("AgentRunSessionBundleReaper: disabled by configuration");
            return false;
        }

        // The reaper is registered only where the agent runtime is enabled, and this service is
        // registered unconditionally — so a runtime-disabled host stops here rather than
        // announcing a sweep it has nothing to run.
        using IServiceScope probe = _scopeFactory.CreateScope();
        if (probe.ServiceProvider.GetService<IAgentRunSessionBundleReaper>() is null)
        {
            _logger.LogInformation(
                "AgentRunSessionBundleReaper: the agent runtime is disabled on this host; nothing to sweep");

            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void OnStarting()
    {
        _logger.LogInformation(
            "AgentRunSessionBundleReaper: starting — sweep every {IntervalMinutes} min, "
                + "max bundle age {MaxAgeDays} d, first sweep in {Delay}",
            _settings.SweepIntervalMinutes,
            _settings.MaxAgeDays,
            StartupDelay);
    }

    /// <inheritdoc/>
    protected override void OnStopped()
    {
        _logger.LogInformation("AgentRunSessionBundleReaper: stopped");
    }

    /// <inheritdoc/>
    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "AgentRunSessionBundleReaper: unhandled error in sweep cycle");
    }

    /// <inheritdoc/>
    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService<IAgentRunSessionBundleReaper>() is not IAgentRunSessionBundleReaper reaper)
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _settings.MaxAgeDays));
        int reaped = await reaper.SweepExpiredAsync(cutoff, cancellationToken);
        if (reaped > 0)
        {
            _logger.LogInformation(
                "AgentRunSessionBundleReaper: deleted {Count} session bundle(s) taken into custody before {Cutoff:O}",
                reaped,
                cutoff);
        }
    }
}
