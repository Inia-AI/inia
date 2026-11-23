using IniaAi.Application.Common.Services;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Storage;

/// <summary>
/// Removes stored files that no row points at, from the stores the platform owns: the database and
/// the stack's own S3 bucket. The rows that point at a stored file are those of
/// <see cref="ReferencingRows"/>, and a schema test fails when another table holds a storage key. A
/// file any of them names stays, whatever store the row records: keys are unique across stores, and
/// a row an older release recorded in one store may hold its bytes in another. A file younger than
/// <see cref="Grace"/> is left, so an upload whose row is not saved yet survives.
/// A MinIO bucket is left alone: a customer's infrastructure team provides it, and it may hold more
/// than the platform's files.
/// </summary>
public sealed class StoredObjectSweep
{
    /// <summary>How many stored files one statement removes.</summary>
    public const int BATCH_SIZE = 500;

    /// <summary>How old an unreferenced file must be before the sweep removes it.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    /// <summary>Every kind of row that points at a stored file, by its store and its key.</summary>
    public static readonly IReadOnlyList<Type> ReferencingRows = [typeof(FileMetadata), typeof(AgentRunSessionBundle)];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ObjectStorageService? _bucket;
    private readonly TimeProvider _clock;
    private readonly ILogger<StoredObjectSweep> _logger;

    public StoredObjectSweep(
        IServiceScopeFactory scopeFactory,
        ILogger<StoredObjectSweep> logger,
        [FromKeyedServices(FileStorageProviders.S3)] ObjectStorageService? bucket = null,
        TimeProvider? clock = null)
    {
        _scopeFactory = scopeFactory;
        _bucket = bucket;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>One sweep of both stores. Returns how many files each lost.</summary>
    public async Task<(int Database, int Bucket)> SweepAsync(CancellationToken cancellationToken)
    {
        DateTime cutoff = _clock.GetUtcNow().UtcDateTime - Grace;
        int database = await SweepDatabaseAsync(cutoff, cancellationToken);
        int bucket = _bucket is null ? 0 : await SweepBucketAsync(_bucket, cutoff, cancellationToken);

        _logger.LogInformation(
            "Stored file sweep removed {DatabaseCount} database copies and {BucketCount} bucket objects that no row points at",
            database,
            bucket);
        return (database, bucket);
    }

    private async Task<int> SweepDatabaseAsync(DateTime cutoff, CancellationToken cancellationToken)
    {
        int removed = 0;
        while (true)
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
            List<string> batch = await UnreferencedCopies(context, cutoff)
                .Select(file => file.Id)
                .Take(BATCH_SIZE)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return removed;
            }

            removed += await UnreferencedCopies(context, cutoff)
                .Where(file => batch.Contains(file.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Database copies that no row names, whatever store the row records: a row an older release
    /// recorded as S3 or MinIO while it wrote the bytes here still holds them.
    /// </summary>
    private static IQueryable<File> UnreferencedCopies(UserDataDbContext context, DateTime cutoff)
    {
        return context.Files.IgnoreQueryFilters()
            .Where(file => file.CreatedAt < cutoff)
            .Where(file => !context.FileMetadata.IgnoreQueryFilters().Any(metadata => metadata.ExternalStoragePath == file.Id))
            .Where(file => !context.AgentRunSessionBundles.IgnoreQueryFilters().Any(bundle => bundle.StorageKey == file.Id));
    }

    private async Task<int> SweepBucketAsync(ObjectStorageService bucket, DateTime cutoff, CancellationToken cancellationToken)
    {
        int removed = 0;
        IAsyncEnumerable<StoredObjectEntry[]> batches = bucket.ListAsync(cancellationToken)
            .Where(entry => entry.LastModified is DateTime written && written.ToUniversalTime() < cutoff)
            .Chunk(BATCH_SIZE);
        await foreach (StoredObjectEntry[] batch in batches)
        {
            removed += await RemoveUnreferencedAsync(bucket, batch, cancellationToken);
        }

        return removed;
    }

    private async Task<int> RemoveUnreferencedAsync(ObjectStorageService bucket, StoredObjectEntry[] batch, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        List<string> keys = [.. batch.Select(entry => entry.Key).Distinct()];

        HashSet<string> referenced = [
            .. await context.FileMetadata.IgnoreQueryFilters()
                .Where(metadata => keys.Contains(metadata.ExternalStoragePath!))
                .Select(metadata => metadata.ExternalStoragePath!)
                .ToListAsync(cancellationToken),
            .. await context.AgentRunSessionBundles.IgnoreQueryFilters()
                .Where(bundle => keys.Contains(bundle.StorageKey))
                .Select(bundle => bundle.StorageKey)
                .ToListAsync(cancellationToken),
        ];

        int removed = 0;
        foreach (StoredObjectEntry entry in batch.Where(entry => !referenced.Contains(entry.Key)))
        {
            await bucket.DeleteObjectAsync(entry.ObjectKey, cancellationToken);
            removed++;
        }

        return removed;
    }
}
