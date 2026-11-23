using IniaAi.Application.Common.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Storage;
using IniaAi.Services.FileDownload;
using IniaAi.Services.Usage;
using IniaAi.WebApi.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Provides extension methods to configure application health checks.
/// </summary>
public static class HealthCheckConfiguration
{
    /// <summary>
    /// Adds health checks for memory, database contexts, DbContext factories, and the S3 and MinIO stores.
    /// </summary>
    /// <param name="services">The service collection to register health checks in.</param>
    /// <param name="configuration">The configuration instance to read the file storage settings from.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddHealthChecksConfigured(this IServiceCollection services, IConfiguration configuration)
    {
        IHealthChecksBuilder healthChecksBuilder = services.AddHealthChecks()
           .AddCheck<MemoryHealthCheck>("Service Memory Check", HealthStatus.Unhealthy, tags: new[] { "Service" })
           .AddDbContextCheck<UserDataDbContext>("UserDataDbContext", HealthStatus.Unhealthy, tags: new[] { "User Database Context" })
           .AddDbContextCheck<CoreDbContext>("SecondDB", HealthStatus.Unhealthy, tags: new[] { "Core Database Context" })
           .AddCheck<DbContextFactoryHealthCheck<UserDataDbContext>>("UserDataDbContext Factory", HealthStatus.Unhealthy, tags: new[] { "User Database Context Factory" })
           .AddCheck<BrowserSandboxHealthCheck>("Browser sandbox", HealthStatus.Degraded, tags: new[] { "Browser" })
           .AddCheck<TokenUsageRecordingHealthCheck>(TokenUsageRecordingHealthCheck.NAME, HealthStatus.Degraded, tags: new[] { "Usage" });

        // Each object store is checked through its own S3 client, Degraded on a failure, so a store
        // that does not answer shows on the health page without taking the host out of rotation.
        FileStorageSettings fileStorage = configuration.GetSection(FileStorageSettings.SECTION_NAME).Get<FileStorageSettings>() ?? new();
        foreach ((string store, string name, bool setUp) in new[]
        {
            (FileStorageProviders.S3, ObjectStoreHealthCheck.S3_NAME, FileStorageProviders.HasS3(fileStorage)),
            (FileStorageProviders.MINIO, ObjectStoreHealthCheck.MINIO_NAME, FileStorageProviders.HasMinio(fileStorage)),
        })
        {
            if (!setUp)
            {
                continue;
            }

            _ = services.AddKeyedSingleton(store, (provider, key) => new ObjectStoreHealthCheck(provider.GetRequiredKeyedService<ObjectStorageService>(key)));
            _ = healthChecksBuilder.Add(new HealthCheckRegistration(
                name,
                provider => provider.GetRequiredKeyedService<ObjectStoreHealthCheck>(store),
                HealthStatus.Degraded,
                ["Storage", store]));
        }

        return services;
    }
}
