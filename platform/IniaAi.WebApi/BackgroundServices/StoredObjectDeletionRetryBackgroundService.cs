using IniaAi.Infrastructure.Storage;
using IniaAi.Services.Jobs;

namespace IniaAi.WebApi.BackgroundServices;

/// <summary>
/// Retries the deletes of stored files that failed, every quarter of an hour. Each delete waits on its
/// own backoff, so most runs find nothing due.
/// </summary>
public sealed class StoredObjectDeletionRetryBackgroundService : PeriodicBackgroundService
{
    private readonly StoredObjectDeletionRetries _retries;
    private readonly ILogger<StoredObjectDeletionRetryBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredObjectDeletionRetryBackgroundService"/> class.
    /// </summary>
    public StoredObjectDeletionRetryBackgroundService(StoredObjectDeletionRetries retries, ILogger<StoredObjectDeletionRetryBackgroundService> logger)
    {
        _retries = retries;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    /// <inheritdoc/>
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(2);

    /// <inheritdoc/>
    protected override async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        int deleted = await _retries.RetryDueAsync(cancellationToken);
        if (deleted > 0)
        {
            _logger.LogInformation("Stored file delete retries deleted {Count} files", deleted);
        }
    }

    /// <inheritdoc/>
    protected override void OnRunFailed(Exception exception)
    {
        _logger.LogError(exception, "Stored file delete retries failed");
    }
}
