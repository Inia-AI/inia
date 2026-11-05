using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Enums;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// Resolves whatever storage service the test hands it, whichever provider is asked for. The
/// house <see cref="TestFileStorageResolver"/> takes <see cref="TestFileStorageService"/> by its
/// concrete type — deliberately, because every hand-built test container in the repository
/// registers it that way — so this is the seam for a test that needs a different one, such as
/// <see cref="ThrowingFileStorageService"/>.
/// </summary>
public class FixedFileStorageResolver : IFileStorageResolver
{
    private readonly IFileStorageService _fileStorageService;

    public FixedFileStorageResolver(IFileStorageService fileStorageService)
    {
        _fileStorageService = fileStorageService;
    }

    public IFileStorageService Resolve(StorageProvider provider)
    {
        return _fileStorageService;
    }

    public IFileStorageService Resolve(string providerName)
    {
        return _fileStorageService;
    }
}
