using IniaAi.Application.Common.Services;
using IniaAi.Common;
using IniaAi.Common.Extensions;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Moves every file the database still holds into the deployment's default store, oldest first and
/// one at a time; the API registers it only when that store is not the database. An organization
/// whose override names another store moves there, and one whose override names the database
/// stays. A file streams across under its own key, its row is pointed at the new store, and only
/// then is the database copy deleted. A file that fails is logged and passed over, so one bad file
/// never holds up the rest; the next start tries it again.
/// <para>
/// It then settles the database copies of files whose row names an object store: an older release
/// without that store recorded it and wrote the bytes to the database, and an older move kept the
/// copy. Each goes to the row's store unless the store holds it already, and then the copy goes.
/// </para>
/// </summary>
public class FileStorageMigrationService : BackgroundService
{
    private const int LOG_PROGRESS_INTERVAL = 10;

    // The pause after each file leaves the database room for the app's own work during a long move.
    private static readonly TimeSpan DefaultPauseBetweenFiles = TimeSpan.FromMilliseconds(100);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FileStorageMigrationService> _logger;
    private readonly FileStorageSettings _fileStorageSettings;
    private readonly TimeSpan _pauseBetweenFiles;

    public FileStorageMigrationService(
        IServiceProvider serviceProvider,
        ILogger<FileStorageMigrationService> logger,
        IOptions<FileStorageSettings> fileStorageOptions,
        TimeSpan? pauseBetweenFiles = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _fileStorageSettings = fileStorageOptions.Value;
        _pauseBetweenFiles = pauseBetweenFiles ?? DefaultPauseBetweenFiles;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting file storage migration service - Database -> {Provider}", _fileStorageSettings.DefaultProvider);

        try
        {
            _ = await MigrateAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("File storage migration was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during file storage migration: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// One pass over the files the database holds. Returns how many moved, were passed over for
    /// having no storage key or belonging in the database, and failed.
    /// </summary>
    public async Task<FileMigrationTally> MigrateAsync(CancellationToken cancellationToken)
    {
        var tally = new FileMigrationTally();
        DateTimeOffset startTime = DateTimeOffset.UtcNow;
        FileInDatabase? after = null;

        _logger.LogInformation("Starting migration scan for files with StorageProvider = Database");

        while (await NextAsync(after, cancellationToken) is { } file)
        {
            after = file;
            if (string.IsNullOrEmpty(file.StorageKey))
            {
                tally.Skipped++;
                _logger.LogWarning("FileMetadata {FileId} has no ExternalStoragePath set, skipping migration", file.Id);
                continue;
            }

            try
            {
                if (await MoveAsync(file, cancellationToken))
                {
                    tally.Migrated++;
                }
                else
                {
                    tally.Skipped++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tally.Errors++;
                _logger.LogError(ex, "Failed to migrate file {FileId}. Error: {ErrorMessage}", file.Id, ex.Message);
            }

            if (tally.Processed % LOG_PROGRESS_INTERVAL == 0)
            {
                _logger.LogInformation("Migration progress: {ProcessedCount} processed | {MigratedCount} migrated | {SkippedCount} skipped | {ErrorCount} errors",
                    tally.Processed, tally.Migrated, tally.Skipped, tally.Errors);
            }

            await Task.Delay(_pauseBetweenFiles, cancellationToken);
        }

        _logger.LogInformation("File storage migration completed! " +
            "Total: {ProcessedCount} | Migrated: {MigratedCount} | Skipped: {SkippedCount} | Errors: {ErrorCount} | Duration: {Duration}",
            tally.Processed, tally.Migrated, tally.Skipped, tally.Errors, DateTimeOffset.UtcNow - startTime);

        if (tally.Errors > 0)
        {
            _logger.LogWarning("{ErrorCount} files failed to migrate. Check error logs above for details.", tally.Errors);
        }

        await SettleCopiesAsync(tally, cancellationToken);
        return tally;
    }

    private async Task SettleCopiesAsync(FileMigrationTally tally, CancellationToken cancellationToken)
    {
        FileInDatabase? after = null;
        while (await NextCopyAsync(after, cancellationToken) is { } file)
        {
            after = file;
            try
            {
                await SettleAsync(file, cancellationToken);
                tally.CopiesSettled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tally.Errors++;
                _logger.LogError(ex, "Failed to settle the database copy of file {FileId}, which its row records in {Provider}", file.Id, file.Provider);
            }

            await Task.Delay(_pauseBetweenFiles, cancellationToken);
        }

        if (tally.CopiesSettled > 0)
        {
            _logger.LogInformation("Settled {CopyCount} database copies of files recorded in an object store", tally.CopiesSettled);
        }
    }

    private async Task<FileInDatabase?> NextCopyAsync(FileInDatabase? after, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        IQueryable<FileMetadata> recordedElsewhere = context.FileMetadata.IgnoreQueryFilters()
            .Where(file => file.StorageProvider == StorageProvider.S3 || file.StorageProvider == StorageProvider.MinIO)
            .Where(file => context.Files.IgnoreQueryFilters().Any(copy => copy.Id == file.ExternalStoragePath));
        if (after is not null)
        {
            recordedElsewhere = recordedElsewhere.Where(file => file.CreatedAt > after.CreatedAt
                || (file.CreatedAt == after.CreatedAt && string.Compare(file.Id, after.Id) > 0));
        }

        return await recordedElsewhere
            .OrderBy(file => file.CreatedAt)
            .ThenBy(file => file.Id)
            .Select(file => new FileInDatabase(file.Id, file.CreatedAt, file.OrganizationId, file.ExternalStoragePath, file.Extension, file.StorageProvider))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task SettleAsync(FileInDatabase file, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        IFileStorageManager storage = scope.ServiceProvider.GetRequiredService<IFileStorageManager>();
        string store = FileStorageProviders.NameOf(file.Provider);

        try
        {
            await using Stream held = await storage.RetrieveFileAsync(store, file.OrganizationId, StorageCategories.Documents, file.StorageKey!);
        }
        catch (FileNotFoundException)
        {
            string contentType = Utils.DetermineMediaTypeFromExtension(file.Extension).ToContentType();
            await using Stream content = await storage.RetrieveFileAsync(FileStorageProviders.DATABASE, file.OrganizationId, StorageCategories.Documents, file.StorageKey!);
            _ = await storage.StoreFileAsync(file.Provider, file.OrganizationId, StorageCategories.Documents, file.StorageKey!, content, contentType);
        }

        string database = FileStorageProviders.DATABASE;
        _ = await context.Files.IgnoreQueryFilters()
            .Where(copy => copy.Id == file.StorageKey)
            .Where(copy => !context.FileMetadata.IgnoreQueryFilters().Any(row =>
                (row.StorageProvider == StorageProvider.Database || row.StorageProvider == StorageProvider.NotDefined) && row.ExternalStoragePath == copy.Id))
            .Where(copy => !context.AgentRunSessionBundles.IgnoreQueryFilters().Any(bundle => bundle.StorageProvider == database && bundle.StorageKey == copy.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<FileInDatabase?> NextAsync(FileInDatabase? after, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        IQueryable<FileMetadata> pending = context.FileMetadata
            .Where(file => !file.IsDeleted && file.StorageProvider == StorageProvider.Database);
        if (after is not null)
        {
            pending = pending.Where(file => file.CreatedAt > after.CreatedAt
                || (file.CreatedAt == after.CreatedAt && string.Compare(file.Id, after.Id) > 0));
        }

        return await pending
            .OrderBy(file => file.CreatedAt)
            .ThenBy(file => file.Id)
            .Select(file => new FileInDatabase(file.Id, file.CreatedAt, file.OrganizationId, file.ExternalStoragePath, file.Extension, file.StorageProvider))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Moves one file into its organization's store. Returns false when that store is the database.</summary>
    private async Task<bool> MoveAsync(FileInDatabase file, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        IFileStorageManager storage = scope.ServiceProvider.GetRequiredService<IFileStorageManager>();
        StorageProvider target = storage.GetProviderForTenant(file.OrganizationId);
        if (target == StorageProvider.Database)
        {
            return false;
        }

        string contentType = Utils.DetermineMediaTypeFromExtension(file.Extension).ToContentType();

        await using (Stream content = await storage.RetrieveFileAsync(FileStorageProviders.DATABASE, file.OrganizationId, StorageCategories.Documents, file.StorageKey!))
        {
            _ = await storage.StoreFileAsync(target, file.OrganizationId, StorageCategories.Documents, file.StorageKey!, content, contentType);
        }

        int repointed = await context.FileMetadata
            .Where(row => row.Id == file.Id && row.StorageProvider == StorageProvider.Database)
            .ExecuteUpdateAsync(row => row.SetProperty(each => each.StorageProvider, target), cancellationToken);

        // A row that changed under the move (deleted, or moved by another host) keeps its own state;
        // the copy just made then has no row and the sweep removes it.
        if (repointed == 1)
        {
            _ = await context.Files.IgnoreQueryFilters()
                .Where(row => row.Id == file.StorageKey)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return true;
    }

    private sealed record FileInDatabase(string Id, DateTime CreatedAt, string OrganizationId, string? StorageKey, string Extension, StorageProvider Provider);
}

/// <summary>What one pass of <see cref="FileStorageMigrationService.MigrateAsync"/> did.</summary>
public sealed class FileMigrationTally
{
    public int Migrated { get; set; }

    public int Skipped { get; set; }

    public int Errors { get; set; }

    /// <summary>Database copies of files recorded in an object store, now only in that store.</summary>
    public int CopiesSettled { get; set; }

    public int Processed => Migrated + Skipped + Errors;
}
