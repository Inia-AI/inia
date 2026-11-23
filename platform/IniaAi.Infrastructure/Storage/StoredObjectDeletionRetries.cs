using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Storage;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Storage;

/// <summary>
/// Records a stored file whose delete failed, and retries the deletes that are due: each failure
/// waits twice as long as the one before, up to a day. A delete still failing after
/// <see cref="ATTEMPTS_BEFORE_ALERT"/> attempts logs <see cref="STUCK_LOG_MARKER"/>, which an alarm
/// reads, and keeps retrying.
/// </summary>
public sealed class StoredObjectDeletionRetries : IStoredObjectDeletionRetries
{
    public const string STUCK_LOG_MARKER = "StoredObjectDeletionStuck";
    public const int ATTEMPTS_BEFORE_ALERT = 8;

    private const int BATCH_SIZE = 100;
    private const int ERROR_LENGTH = 1000;

    private static readonly TimeSpan FirstWait = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LongestWait = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileStorageResolver _resolver;
    private readonly TimeProvider _clock;
    private readonly ILogger<StoredObjectDeletionRetries> _logger;

    public StoredObjectDeletionRetries(
        IServiceScopeFactory scopeFactory,
        IFileStorageResolver resolver,
        ILogger<StoredObjectDeletionRetries> logger,
        TimeProvider? clock = null)
    {
        _scopeFactory = scopeFactory;
        _resolver = resolver;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task RecordAsync(string storageProvider, string organizationId, string category, string key, Exception failure)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            DateTime now = _clock.GetUtcNow().UtcDateTime;
            _ = context.StoredObjectDeletions.Add(new StoredObjectDeletion
            {
                OrganizationId = organizationId,
                StorageProvider = storageProvider,
                Category = category,
                StorageKey = key,
                Attempts = 1,
                LastError = Shorten(failure.Message),
                CreatedAt = now,
                NextAttemptAt = now + FirstWait,
            });
            _ = await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A failed delete of stored file {StorageKey} in {StorageProvider} could not be recorded; the sweep removes the file", key, storageProvider);
        }
    }

    /// <summary>Retries every delete that is due. Returns how many deleted.</summary>
    public async Task<int> RetryDueAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        List<StoredObjectDeletion> due = await context.StoredObjectDeletions
            .Where(deletion => deletion.NextAttemptAt <= now)
            .OrderBy(deletion => deletion.NextAttemptAt)
            .Take(BATCH_SIZE)
            .ToListAsync(cancellationToken);

        int deleted = 0;
        foreach (StoredObjectDeletion deletion in due)
        {
            try
            {
                await _resolver.Resolve(deletion.StorageProvider).DeleteAsync(deletion.OrganizationId, deletion.Category, deletion.StorageKey);
                _ = context.StoredObjectDeletions.Remove(deletion);
                deleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                deletion.Attempts++;
                deletion.LastError = Shorten(ex.Message);
                deletion.NextAttemptAt = now + WaitAfter(deletion.Attempts);
                if (deletion.Attempts >= ATTEMPTS_BEFORE_ALERT)
                {
                    _logger.LogError(ex, STUCK_LOG_MARKER + ": the delete of stored file {StorageKey} in {StorageProvider} failed {Attempts} times", deletion.StorageKey, deletion.StorageProvider, deletion.Attempts);
                }
            }
        }

        _ = await context.SaveChangesAsync(cancellationToken);
        return deleted;
    }

    private static TimeSpan WaitAfter(int attempts)
    {
        double minutes = FirstWait.TotalMinutes * Math.Pow(2, attempts - 1);
        return minutes >= LongestWait.TotalMinutes ? LongestWait : TimeSpan.FromMinutes(minutes);
    }

    private static string Shorten(string message)
    {
        return message.Length <= ERROR_LENGTH ? message : message[..ERROR_LENGTH];
    }
}
