using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Enums;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// Test implementation of file storage resolver that returns the test file storage service
/// </summary>
public class TestFileStorageResolver : IFileStorageResolver
{
    private readonly TestFileStorageService _fileStorageService;

    public TestFileStorageResolver(TestFileStorageService fileStorageService)
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
