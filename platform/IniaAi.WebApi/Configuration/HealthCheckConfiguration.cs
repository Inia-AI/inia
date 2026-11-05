using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using IniaAi.Services.FileDownload;
using IniaAi.Services.Usage;

namespace IniaAi.WebApi.Configuration;

/// <summary>
/// Provides extension methods to configure application health checks.
/// </summary>
public static class HealthCheckConfiguration
{
    /// <summary>
    /// Adds health checks for memory, database contexts, DbContext factories, and MinIO storage.
    /// </summary>
    /// <param name="services">The service collection to register health checks in.</param>
    /// <param name="configuration">The configuration instance to read MinIO settings from.</param>
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

        // Add MinIO health check if MinIO is configured
        IConfigurationSection fileStorageSection = configuration.GetSection(FileStorageSettings.SECTION_NAME);
        MinioStorageSettings? minioSettings = fileStorageSection.GetSection("MinIO").Get<MinioStorageSettings>();

        if (minioSettings is not null && !string.IsNullOrEmpty(minioSettings.Endpoint))
        {
            // Build the health check URL with the correct protocol
            string endpoint = minioSettings.Endpoint.Trim();
            string healthCheckUrl;

            if (endpoint.StartsWith("http://") || endpoint.StartsWith("https://"))
            {
                // Endpoint already has protocol
                healthCheckUrl = $"{endpoint}/minio/health/ready";
            }
            else
            {
                // Endpoint doesn't have protocol, add it based on UseSSL setting
                string protocol = minioSettings.UseSSL ? "https" : "http";
                healthCheckUrl = $"{protocol}://{endpoint}/minio/health/ready";
            }

            Log.Information("MinIO health check configured with URL: {HealthCheckUrl}", healthCheckUrl);

            // Bypass system proxy (e.g. Zscaler) — MinIO is always internal infrastructure
            // and .NET doesn't support CIDR notation in NO_PROXY.
            // The handler must be passed directly to AddUrlGroup because it internally calls
            // ConfigurePrimaryHttpMessageHandler, which would overwrite any prior configuration.
            _ = services.AddTransient<LoggingDelegatingHandler>();
            _ = services.AddHttpClient("MinIO Storage")
                .AddHttpMessageHandler<LoggingDelegatingHandler>();

            _ = healthChecksBuilder.AddUrlGroup(new Uri(healthCheckUrl),
                name: "MinIO Storage",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "Storage", "MinIO" },
                timeout: TimeSpan.FromSeconds(10),
                configurePrimaryHttpMessageHandler: _ => new HttpClientHandler { UseProxy = false });
        }

        return services;
    }
}
