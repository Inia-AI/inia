using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.Testing.DependencyInjection;

public static class TestFileStorageServices
{
    /// <summary>
    /// The real <see cref="FileStorageManager"/> over one in-memory <see cref="TestFileStorageService"/>
    /// that every consumer in the provider shares.
    /// </summary>
    public static IServiceCollection AddTestFileStorage(this IServiceCollection services)
    {
        _ = services.AddSingleton(Options.Create(new FileStorageSettings { DefaultProvider = nameof(StorageProvider.Database) }));
        _ = services.AddSingleton<TestFileStorageService>();
        _ = services.AddSingleton<IFileStorageResolver, TestFileStorageResolver>();
        _ = services.AddScoped<IFileStorageManager, FileStorageManager>();

        return services;
    }
}
