using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// Real storage that runs one interruption before its first upload, so a test can see what a
/// second caller finds while the first caller is storing a document's bytes. Every call is the
/// wrapped storage's own.
/// </summary>
public sealed class InterruptedFileStorageService : IFileStorageService
{
    private readonly IFileStorageService _storage;
    private Func<Task>? _interruption;

    public InterruptedFileStorageService(IFileStorageService storage)
    {
        _storage = storage;
    }

    public void InterruptFirstUploadWith(Func<Task> interruption)
    {
        _interruption = interruption;
    }

    public async Task<string> UploadAsync(string organizationId, string category, string key, Stream content, string contentType)
    {
        if (Interlocked.Exchange(ref _interruption, null) is { } interruption)
        {
            await interruption();
        }

        return await _storage.UploadAsync(organizationId, category, key, content, contentType);
    }

    public Task<Stream> DownloadAsync(string organizationId, string category, string key)
    {
        return _storage.DownloadAsync(organizationId, category, key);
    }

    public Task DeleteAsync(string organizationId, string category, string key)
    {
        return _storage.DeleteAsync(organizationId, category, key);
    }
}

public static class InterruptedDatabaseStorageRegistration
{
    /// <summary>
    /// The database storage behind <see cref="InterruptedFileStorageService"/>, as every provider
    /// resolves it. Pair it with <see cref="TestDatabaseServices.AddSqliteTestDatabaseServices"/>,
    /// so the stored files have the primary key the platform's database enforces.
    /// </summary>
    public static IServiceCollection AddInterruptedDatabaseStorage(this IServiceCollection services)
    {
        _ = services.AddSingleton(provider => new InterruptedFileStorageService(
            new DatabaseStorageService(provider.GetRequiredService<IServiceScopeFactory>())));
        _ = services.AddSingleton<IFileStorageService>(provider => provider.GetRequiredService<InterruptedFileStorageService>());
        _ = services.AddSingleton<IFileStorageResolver, FixedFileStorageResolver>();
        _ = services.AddSingleton(Options.Create(new FileStorageSettings { DefaultProvider = nameof(StorageProvider.Database) }));
        return services.AddTransient<IFileStorageManager, FileStorageManager>();
    }
}
