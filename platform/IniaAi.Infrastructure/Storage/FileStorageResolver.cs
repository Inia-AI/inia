using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Storage;

/// <summary>
/// Resolves the storage implementation based on a configured key.
/// </summary>
public class FileStorageResolver : IFileStorageResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FileStorageResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileStorageResolver"/>.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving keyed services.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public FileStorageResolver(IServiceProvider serviceProvider, ILogger<FileStorageResolver> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public IFileStorageService Resolve(string providerName)
    {
        _logger.LogDebug("Attempting to resolve storage provider: {ProviderName}", providerName);

        // Normalize provider name
        string normalizedName = providerName switch
        {
            null or "" or "Database" => "Database",
            "S3" => "S3",
            "Minio" or "MinIO" => "Minio",
            _ => providerName
        };

        _logger.LogDebug("Normalized provider name: {NormalizedName}", normalizedName);

        try
        {
            IFileStorageService service = _serviceProvider.GetRequiredKeyedService<IFileStorageService>(normalizedName);
            _logger.LogDebug("Successfully resolved storage provider: {NormalizedName}", normalizedName);
            return service;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Failed to resolve storage provider '{NormalizedName}': {Error}", normalizedName, ex.Message);

            // If the requested provider isn't available, fall back to Database
            if (normalizedName != "Database")
            {
                _logger.LogInformation("Attempting fallback to Database storage provider");
                try
                {
                    IFileStorageService fallbackService = _serviceProvider.GetRequiredKeyedService<IFileStorageService>("Database");
                    _logger.LogInformation("Successfully resolved fallback Database storage provider");
                    return fallbackService;
                }
                catch (InvalidOperationException fallbackEx)
                {
                    _logger.LogError("Fallback to Database storage provider also failed: {FallbackError}", fallbackEx.Message);
                    throw new InvalidOperationException($"No storage provider found for '{providerName}' and fallback to Database also failed. Original error: {ex.Message}. Fallback error: {fallbackEx.Message}");
                }
            }

            _logger.LogError("Database storage provider failed to resolve: {Error}", ex.Message);
            throw new InvalidOperationException($"No storage provider found for '{providerName}'. Error: {ex.Message}. Available providers depend on configuration.");
        }
    }

    /// <summary>
    /// Resolves the storage implementation based on a configured enum value.
    /// </summary>
    public IFileStorageService Resolve(Domain.Enums.StorageProvider provider)
    {
        string providerName = provider switch
        {
            Domain.Enums.StorageProvider.Database => "Database",
            Domain.Enums.StorageProvider.S3 => "S3",
            Domain.Enums.StorageProvider.MinIO => "Minio",
            _ => "Database"
        };

        return Resolve(providerName);
    }
}
