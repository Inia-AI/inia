using IniaAi.Infrastructure.Storage;
using IniaAi.Services.Jobs;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// Removes stored files that no row points at, once a day (<see cref="StoredObjectSweep"/>).
/// </summary>
public sealed class StoredObjectSweepBackgroundService : PeriodicBackgroundService
{
    private readonly StoredObjectSweep _sweep;
    private readonly ILogger<StoredObjectSweepBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredObjectSweepBackgroundService"/> class.
    /// </summary>
    public StoredObjectSweepBackgroundService(StoredObjectSweep sweep, ILogger<StoredObjectSweepBackgroundService> logger)
    {
        _sweep = sweep;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    /// <inheritdoc/>
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(30);

    /// <inheritdoc/>
    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        _ = await _sweep.SweepAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "Stored file sweep failed");
    }
}
