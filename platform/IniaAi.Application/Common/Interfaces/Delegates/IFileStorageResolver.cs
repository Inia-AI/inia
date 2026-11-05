using IniaAi.Application.Common.Interfaces.Services;

namespace IniaAi.Application.Common.Interfaces.Delegates;

/// <summary>
/// Resolves a concrete file storage implementation based on logic or config.
/// </summary>
public interface IFileStorageResolver
{
    /// <summary>
    /// Returns an appropriate storage service for the given provider key.
    /// </summary>
    /// <param name="providerName">A key like "MinIO" or "S3".</param>
    /// <returns>The resolved IFileStorageService implementation.</returns>
    IFileStorageService Resolve(string providerName);

    /// <summary>
    /// Returns an appropriate storage service for the given provider enum.
    /// </summary>
    /// <param name="provider">The storage provider enum value.</param>
    /// <returns>The resolved IFileStorageService implementation.</returns>
    IFileStorageService Resolve(Domain.Enums.StorageProvider provider);
}
