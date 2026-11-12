using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Background service for migrating file blobs from database storage to MinIO
/// </summary>
public class FileStorageMigrationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FileStorageMigrationService> _logger;
    private readonly FileStorageSettings _fileStorageSettings;

    public FileStorageMigrationService(
        IServiceProvider serviceProvider,
        ILogger<FileStorageMigrationService> logger,
        IOptions<FileStorageSettings> fileStorageOptions)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _fileStorageSettings = fileStorageOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Only run migration if default provider is not Database
        if (_fileStorageSettings.DefaultProvider.Equals("Database", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("File storage migration skipped - default provider is Database");
            return;
        }

        _logger.LogInformation("Starting file storage migration service - Database -> {Provider}", _fileStorageSettings.DefaultProvider);

        try
        {
            await MigrateFilesAsync(stoppingToken);

            if (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("File storage migration was cancelled before completion");
            }
            else
            {
                _logger.LogInformation("File storage migration service completed successfully");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("File storage migration was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during file storage migration: {ErrorMessage}", ex.Message);
            throw; // Re-throw to ensure the background service is marked as failed
        }
    }

    private async Task MigrateFilesAsync(CancellationToken cancellationToken)
    {
        int processedCount = 0;
        int migratedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;
        DateTimeOffset startTime = DateTimeOffset.UtcNow;
        const int logProgressInterval = 10; // Log progress every 10 files

        _logger.LogInformation("Starting migration scan for files with StorageProvider = Database");

        while (!cancellationToken.IsCancellationRequested)
        {
            // Get the ID of the next file that needs migration
            string? fileMetadataId = await GetNextDatabaseFileIdAsync();

            if (fileMetadataId is null)
            {
                // No more files to migrate
                _logger.LogInformation("No more files found with StorageProvider = Database. Migration scan complete.");
                break;
            }

            try
            {
                await MigrateSingleFileAsync(fileMetadataId, cancellationToken);
                migratedCount++;

                _logger.LogDebug("Successfully migrated file with ID {FileId} to {Provider}",
                    fileMetadataId, _fileStorageSettings.DefaultProvider);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("has no bytes to migrate"))
            {
                // This is a skipped file, not an error
                skippedCount++;
                _logger.LogDebug("Skipped file {FileId} - {Reason}",
                    fileMetadataId, ex.Message);
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "Failed to migrate file {FileId}. Error: {ErrorMessage}",
                    fileMetadataId, ex.Message);
            }

            processedCount++;

            // Log progress periodically to avoid log spam
            if (processedCount % logProgressInterval == 0)
            {
                TimeSpan elapsed = DateTimeOffset.UtcNow - startTime;
                double avgTimePerFile = elapsed.TotalSeconds / processedCount;
                _logger.LogInformation("Migration progress: {ProcessedCount} processed | {MigratedCount} migrated | {SkippedCount} skipped | {ErrorCount} errors | Avg: {AvgTime:F2}s/file",
                    processedCount, migratedCount, skippedCount, errorCount, avgTimePerFile);
            }

            // Add a small delay to prevent overwhelming the system
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        TimeSpan totalElapsed = DateTimeOffset.UtcNow - startTime;
        double successRate = processedCount > 0 ? (migratedCount * 100.0 / processedCount) : 0;

        _logger.LogInformation("File storage migration completed! " +
            "Total: {ProcessedCount} | Migrated: {MigratedCount} | Skipped: {SkippedCount} | Errors: {ErrorCount} | " +
            "Success Rate: {SuccessRate:F1}% | Duration: {Duration}",
            processedCount, migratedCount, skippedCount, errorCount, successRate, totalElapsed);

        if (errorCount > 0)
        {
            _logger.LogWarning("{ErrorCount} files failed to migrate. Check error logs above for details.", errorCount);
        }
    }

    private async Task<string?> GetNextDatabaseFileIdAsync()
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        // Get the first FileMetadata ID with StorageProvider = Database
        var fileMetadata = await unitOfWork.FileMetadata.GetAll()
            .Where(x => !x.IsDeleted && x.StorageProvider == StorageProvider.Database)
            .OrderBy(x => x.CreatedAt) // Order by creation time to process oldest first
            .Select(x => new { x.Id, x.ExternalStoragePath, x.Name })
            .FirstOrDefaultAsync();

        if (fileMetadata is null)
        {
            // This is expected when no more files need migration
            return null;
        }

        if (string.IsNullOrEmpty(fileMetadata.ExternalStoragePath))
        {
            _logger.LogWarning("FileMetadata {FileId} (file name of {FileNameLength} characters) has no ExternalStoragePath set, skipping migration",
                fileMetadata.Id, fileMetadata.Name.Length);
            return null;
        }

        return fileMetadata.Id;
    }

    private async Task MigrateSingleFileAsync(string fileMetadataId, CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        IFileStorageManager fileStorageManager = scope.ServiceProvider.GetRequiredService<IFileStorageManager>();

        // Load FileMetadata in this context
        FileMetadata? fileMetadata = await unitOfWork.FileMetadata.GetByIdAsync(fileMetadataId, cancellationToken)
            ?? throw new InvalidOperationException($"FileMetadata with ID {fileMetadataId} not found");

        if (string.IsNullOrEmpty(fileMetadata.ExternalStoragePath))
        {
            throw new InvalidOperationException($"File {fileMetadata.Id} has no ExternalStoragePath to migrate");
        }

        // Load the corresponding file with bytes
        Domain.Entities.Documents.File? file = await unitOfWork.Files.GetByIdAsync(fileMetadata.ExternalStoragePath, cancellationToken)
            ?? throw new InvalidOperationException($"File entity not found for FileMetadata {fileMetadata.Id} with path {fileMetadata.ExternalStoragePath}");

        // Check if file has bytes to migrate
        if (file.Bytes.Length == 0)
        {
            _logger.LogWarning("Skipping file {FileId} (file name of {FileNameLength} characters) - no bytes loaded or empty file",
                fileMetadata.ExternalStoragePath, fileMetadata.Name.Length);
            return;
        }

        try
        {
            double fileSizeMB = file.Bytes.Length / (1024.0 * 1024.0);
            _logger.LogDebug("Processing file {FileId} (file name of {FileNameLength} characters) - Size: {FileSizeMB:F2} MB, Type: {ContentType}",
                fileMetadata.ExternalStoragePath, fileMetadata.Name.Length, fileSizeMB, fileMetadata.ContentType);

            // Migrate file to new storage using IFileStorageManager
            string key = fileMetadata.ExternalStoragePath;
            string newPath = await fileStorageManager.StoreFileAsync(
                _fileStorageSettings.DefaultProvider,
                fileMetadata.OrganizationId,
                StorageCategories.Documents,
                key,
                file.Bytes,
                fileMetadata.ContentType);

            _logger.LogDebug("Stored file {FileId} at new path: {NewPath}", fileMetadata.Id, newPath);

            // Update FileMetadata to point to new storage
            StorageProvider oldProvider = fileMetadata.StorageProvider;
            string oldPath = fileMetadata.ExternalStoragePath;

            fileMetadata.StorageProvider = GetStorageProviderEnum(_fileStorageSettings.DefaultProvider);
            fileMetadata.ExternalStoragePath = newPath;

            // Clear bytes from database file (keep the File entity for reference)
            //file.LoadBytes(Array.Empty<byte>());

            _ = await unitOfWork.CompleteAsync(cancellationToken);

            _logger.LogDebug("Updated FileMetadata: {OldProvider} -> {NewProvider}, Path: {OldPath} -> {NewPath}",
                oldProvider, fileMetadata.StorageProvider, oldPath, newPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage operation failed for file {FileId} (file name of {FileNameLength} characters): {ErrorMessage}",
                fileMetadata.ExternalStoragePath, fileMetadata.Name.Length, ex.Message);
            throw; // Re-throw to be caught by the calling method
        }
    }

    private static StorageProvider GetStorageProviderEnum(string providerName)
    {
        return providerName switch
        {
            "MinIO" or "Minio" => StorageProvider.MinIO,
            "S3" => StorageProvider.S3,
            "Database" => StorageProvider.Database,
            _ => StorageProvider.Database
        };
    }
}
