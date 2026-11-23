using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Storage;

/// <summary>
/// Resolves the storage implementation based on a configured key. A store the deployment does not
/// set up is refused: falling back to another store would read, or write, a file where its row
/// does not say it is.
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
        string store = FileStorageProviders.NameOf(FileStorageProviders.ParseKnown(providerName));

        IFileStorageService? service = _serviceProvider.GetKeyedService<IFileStorageService>(store);
        if (service is null)
        {
            _logger.LogError("A file is stored in {Store}, which this deployment does not set up", store);
            throw new InvalidOperationException($"This file is stored in {store}, which this deployment does not set up.");
        }

        return service;
    }

    /// <summary>
    /// Resolves the storage implementation based on a configured enum value.
    /// </summary>
    public IFileStorageService Resolve(Domain.Enums.StorageProvider provider)
    {
        return Resolve(FileStorageProviders.NameOf(provider));
    }
}
