using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Configuration.Platform;
using IniaAi.Services.Jobs;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// Periodically reclaims storage from workflow-version history by clearing the saved JSON of versions
/// outside the configured keep window, on a weekly cadence covering the whole deployment in one pass.
/// Does nothing unless an operator sets <see cref="WorkflowVersionRetentionSettings.Mode"/> away from
/// <see cref="WorkflowVersionRetentionMode.Disabled"/>. The actual keep/clear decision and logging
/// live in <see cref="IWorkflowVersionRetentionService"/>.
/// </summary>
public class WorkflowVersionPayloadTrimBackgroundService : PeriodicBackgroundService
{
    private const double MinDaysBetweenRuns = 6;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<WorkflowVersionRetentionSettings> _settings;
    private readonly ILogger<WorkflowVersionPayloadTrimBackgroundService> _logger;
    private DateTime? _lastTrimAt;

    /// <summary>
    /// Creates the service with the dependencies needed to resolve a scoped retention service per
    /// cycle and to read the deployment's retention settings.
    /// </summary>
    public WorkflowVersionPayloadTrimBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkflowVersionRetentionSettings> settings,
        ILogger<WorkflowVersionPayloadTrimBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    /// <inheritdoc/>
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    protected override void OnStarting()
    {
        _logger.LogInformation(
            "WorkflowVersionRetention: background service started in {Mode} mode; first check after {Delay}.",
            _settings.Value.Mode, StartupDelay);
    }

    /// <inheritdoc/>
    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "WorkflowVersionRetention: Unhandled error in trim cycle");
    }

    /// <inheritdoc/>
    protected override async Task RunOnceAsync(CancellationToken ct)
    {
        WorkflowVersionRetentionSettings settings = _settings.Value;
        if (settings.Mode == WorkflowVersionRetentionMode.Disabled)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (!IsWithinTrimWindow(now, settings.TrimDayOfWeek, settings.TrimHourUtc))
        {
            return;
        }

        if (IsThrottled(now))
        {
            return;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        IWorkflowVersionRetentionService retentionService =
            scope.ServiceProvider.GetRequiredService<IWorkflowVersionRetentionService>();

        var policy = new WorkflowVersionRetentionPolicy(
            settings.KeepLastNPayloads,
            settings.KeepNewerThanDays,
            settings.Mode == WorkflowVersionRetentionMode.ReportOnly,
            settings.MaxPayloadsTrimmedPerRun);

        try
        {
            _ = await retentionService.TrimAsync(policy, ct);
            _lastTrimAt = now;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WorkflowVersionRetention: trim run failed");
        }
    }

    internal static bool IsWithinTrimWindow(DateTime nowUtc, DayOfWeek trimDayOfWeek, int trimHourUtc)
    {
        return nowUtc.DayOfWeek == trimDayOfWeek && nowUtc.Hour >= trimHourUtc;
    }

    private bool IsThrottled(DateTime nowUtc)
    {
        return _lastTrimAt is DateTime lastRun && (nowUtc - lastRun).TotalDays < MinDaysBetweenRuns;
    }
}
