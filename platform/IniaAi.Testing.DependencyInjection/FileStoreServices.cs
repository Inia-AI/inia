using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Testing.DependencyInjection;

public static class FileStoreServices
{
    /// <summary>
    /// The real database store, resolver and storage manager over whatever <c>UserDataDbContext</c>
    /// the test registers, with <paramref name="defaultProvider"/> as the default store. A test adds
    /// the other stores it needs under their keyed names.
    /// </summary>
    public static IServiceCollection AddFileStores(this IServiceCollection services, string defaultProvider)
    {
        _ = services.AddSingleton(Options.Create(new FileStorageSettings { DefaultProvider = defaultProvider }));
        _ = services.AddKeyedSingleton<IFileStorageService>(FileStorageProviders.DATABASE, (provider, _) => new DatabaseStorageService(provider.GetRequiredService<IServiceScopeFactory>()));
        _ = services.AddSingleton<IFileStorageResolver>(provider => new FileStorageResolver(provider, NullLogger<FileStorageResolver>.Instance));
        _ = services.AddScoped<IFileStorageManager, FileStorageManager>();
        return services;
    }
}
