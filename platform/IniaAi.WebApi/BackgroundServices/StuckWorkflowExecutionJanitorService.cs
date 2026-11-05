using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Services.Jobs;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// Periodic shell around <see cref="StuckWorkflowExecutionJanitor"/> — the sweep that fails
/// executions a dead host stranded (see <see cref="WorkflowExecutionJanitorSettings"/> for
/// the policy). One instance in WebApi covers the deployment; the guarded terminal write
/// keeps a redundant sweep harmless.
/// </summary>
public class StuckWorkflowExecutionJanitorService : PeriodicBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkflowExecutionJanitorSettings _settings;
    private readonly ILogger<StuckWorkflowExecutionJanitorService> _logger;

    /// <summary>Takes the scope factory each sweep cycle resolves the sweep service from.</summary>
    public StuckWorkflowExecutionJanitorService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkflowExecutionJanitorSettings> settings,
        ILogger<StuckWorkflowExecutionJanitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, _settings.SweepIntervalMinutes));

    /// <inheritdoc/>
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(2);

    /// <inheritdoc/>
    protected override bool ShouldStart()
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("StuckExecutionJanitor: disabled by configuration");
        }

        return _settings.Enabled;
    }

    /// <inheritdoc/>
    protected override void OnStarting()
    {
        _logger.LogInformation(
            "StuckExecutionJanitor: starting — sweep every {IntervalMinutes} min, "
                + "max execution lifetime {MaxLifetimeHours} h, first sweep in {Delay}",
            _settings.SweepIntervalMinutes,
            _settings.MaxLifetimeHours,
            StartupDelay);
    }

    /// <inheritdoc/>
    protected override void OnStopped()
    {
        _logger.LogInformation("StuckExecutionJanitor: stopped");
    }

    /// <inheritdoc/>
    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "StuckExecutionJanitor: unhandled error in sweep cycle");
    }

    /// <inheritdoc/>
    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        StuckWorkflowExecutionJanitor janitor =
            scope.ServiceProvider.GetRequiredService<StuckWorkflowExecutionJanitor>();

        int marked = await janitor.SweepOnceAsync(cancellationToken);
        if (marked > 0)
        {
            _logger.LogInformation("StuckExecutionJanitor: failed {Count} stuck execution(s)", marked);
        }
    }
}
