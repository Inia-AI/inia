using IniaAi.Application.Common.Services;
using IniaAi.Configuration;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Hosting;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Storage;
using IniaAi.Services.Jobs;
using IniaAi.WebApi.BackgroundServices;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Extensions;
using IniaAi.WebApi.Filters;

namespace IniaAi.WebApi;

/// <summary>
/// Registers presentation layer services.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds presentation layer services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPresentation(this IServiceCollection services, WebApplicationBuilder builder)
    {
        PlatformSettings platformSettings = builder.Configuration.ConfigurePlatformSettings(builder);

        _ = services.AddConfiguredControllers();
        _ = services.AddConfiguredApiVersioning();
        _ = services.AddCorsWithPolicy(builder.Configuration);
        _ = services.AddClientForwardedHeaders(builder.Configuration);
        _ = services.AddSignInRateLimiting(builder.Configuration);
        _ = services.AddAuthentication(builder.Configuration);
        _ = services.AddSwaggerDocs(builder.Configuration);
        _ = services.AddProblemDetails();

        services.AddTransientServices(platformSettings);
        services.AddScopedServices();
        services.AddSingletonServices(platformSettings);

        _ = services.AddHealthChecksConfigured(builder.Configuration);

        _ = services.AddExceptionHandler<WebApiExceptionHandlerFilter>();

        _ = services.AddIniaAiDataProtection(builder.Configuration);

        _ = builder.Services.AddMemoryCache();
        _ = builder.Services.AddDistributedMemoryCache();
        _ = builder.Services.AddHttpContextAccessor();

        FileStorageSettings? fileStorageSection = builder.Configuration.GetSection(FileStorageSettings.SECTION_NAME).Get<FileStorageSettings>();

        if (FileStorageProviders.Normalize(fileStorageSection?.DefaultProvider) != FileStorageProviders.DATABASE)
        {
            _ = services.AddHostedService<FileStorageMigrationService>();
        }

        // Register background services
        _ = services.AddHostedService<ModelPricingSeedingService>();
        _ = services.AddHostedService<SoftDeleteCleanupBackgroundService>();
        _ = services.AddHostedService<StoredObjectDeletionRetryBackgroundService>();
        _ = services.AddHostedService<StoredObjectSweepBackgroundService>();
        _ = services.AddHostedService<StuckWorkflowExecutionJanitorService>();
        _ = services.AddHostedService<HostLossSweepService>();
        _ = services.AddHostBootHeartbeat();
        _ = services.AddHostedService<TokenUsageTotalsSweepService>();
        _ = services.AddHostedService<AgentRunSessionBundleReaperService>();
        _ = services.AddHostedService<AgentConversationRetentionService>();
        _ = services.AddHostedService<WorkflowVersionPayloadTrimBackgroundService>();
        _ = services.AddHostedService<JobQueueShutdownService>();

        return services;
    }
}
