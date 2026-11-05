using IniaAi.Configuration.Platform;
using IniaAi.Infrastructure.Services;
using IniaAi.Services.Jobs;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// Periodic shell around <see cref="TokenUsageTotalsSweep"/>. Every WebApi host runs it; the sweep's
/// database lock lets one host at a time do the work.
/// </summary>
public class TokenUsageTotalsSweepService : PeriodicBackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TokenUsageTotalsSettings _settings;
    private readonly ILogger<TokenUsageTotalsSweepService> _logger;

    /// <summary>Takes the scope factory each sweep cycle resolves the sweep from.</summary>
    public TokenUsageTotalsSweepService(
        IServiceScopeFactory scopeFactory,
        IOptions<TokenUsageTotalsSettings> settings,
        ILogger<TokenUsageTotalsSweepService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(1, _settings.SweepIntervalSeconds));

    /// <inheritdoc/>
    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    protected override bool ShouldStart()
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("TokenUsageTotals: disabled by configuration");
        }

        return _settings.Enabled;
    }

    /// <inheritdoc/>
    protected override void OnStarting()
    {
        _logger.LogInformation(
            "TokenUsageTotals: starting — sweep every {IntervalSeconds} s, first sweep in {Delay}",
            _settings.SweepIntervalSeconds,
            StartupDelay);
    }

    /// <inheritdoc/>
    protected override void OnStopped()
    {
        _logger.LogInformation("TokenUsageTotals: stopped");
    }

    /// <inheritdoc/>
    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        TokenUsageTotalsSweep sweep = scope.ServiceProvider.GetRequiredService<TokenUsageTotalsSweep>();

        int added = await sweep.SweepOnceAsync(cancellationToken);
        if (added > 0)
        {
            _logger.LogInformation("TokenUsageTotals: added {Count} ledger row(s) to the totals", added);
        }
    }

    /// <inheritdoc/>
    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "TokenUsageTotals: unhandled error in sweep cycle");
    }
}
