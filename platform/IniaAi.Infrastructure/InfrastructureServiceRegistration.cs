using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Azure;
using IniaAi.AiConnection.Cohere;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.TokenProviders;
using IniaAi.Application.Auditing;
using IniaAi.Application.Common.Interfaces.Delegates;
using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Documents;
using IniaAi.Common.Interfaces.GitHub;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Configuration;
using IniaAi.Configuration.Email;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Storage;
using IniaAi.Core.Documents;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Email;
using IniaAi.Infrastructure.GitHub;
using IniaAi.Infrastructure.Persistence.Services;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Minio;
using System.Net.Http.Headers;

namespace IniaAi.Infrastructure;

public static class InfrastructureServiceRegistration
{
    /// <summary>
    /// Registers the SMTP, SendGrid and SES senders and the outgoing mail that picks between them.
    /// </summary>
    public static IServiceCollection AddOutgoingMail(this IServiceCollection services, IConfiguration configuration)
    {
        _ = services.Configure<SendGridSettings>(configuration.GetSection(SendGridSettings.SECTION_NAME));
        _ = services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SECTION_NAME));
        _ = services.Configure<SesSettings>(configuration.GetSection(SesSettings.SECTION_NAME));
        _ = services.AddSingleton<IOutgoingMail>(provider =>
            new OutgoingMail(OutgoingMail.ConfiguredSender(configuration[OutgoingMail.PROVIDER_KEY], provider)));

        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure settings
        _ = services.AddOutgoingMail(configuration);
        _ = services.Configure<FileStorageSettings>(configuration.GetSection(FileStorageSettings.SECTION_NAME));
        _ = services.Configure<GitHubSettings>(configuration.GetSection("PlatformSettings:GitHub"));
        OutboundAccessSettings.ThrowIfAllowListInvalid(configuration);
        _ = services.AddScoped<IGitHubWorkflowVersioningService, GitHubWorkflowVersioningService>();

        _ = services.AddHttpClient("GitHubContents", client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IniaAi-Platform/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });

        // Configure MinIO client
        IConfigurationSection fileStorageSection = configuration.GetSection(FileStorageSettings.SECTION_NAME);
        MinioStorageSettings? minioSettings = fileStorageSection.GetSection("MinIO").Get<MinioStorageSettings>();
        if (minioSettings is not null && !string.IsNullOrEmpty(minioSettings.Endpoint))
        {
            _ = services.AddSingleton(sp =>
            {
                // MinIO client expects endpoint without protocol prefix
                string endpoint = minioSettings.Endpoint;
                if (endpoint.StartsWith("http://"))
                {
                    endpoint = endpoint[7..]; // Remove "http://"
                }
                else if (endpoint.StartsWith("https://"))
                {
                    endpoint = endpoint[8..]; // Remove "https://"
                }

                // Bypass system proxy (e.g. Zscaler) for MinIO — it's always internal infrastructure.
                // .NET doesn't support CIDR notation in NO_PROXY, so direct IP endpoints
                // get incorrectly routed through the proxy.
                var httpClient = new HttpClient(new HttpClientHandler { UseProxy = false });

                IMinioClient client = new MinioClient()
                    .WithEndpoint(endpoint)
                    .WithCredentials(minioSettings.AccessKey, minioSettings.SecretKey)
                    .WithHttpClient(httpClient);

                if (minioSettings.UseSSL)
                {
                    client = client.WithSSL();
                }

                if (!string.IsNullOrEmpty(minioSettings.Region))
                {
                    client = client.WithRegion(minioSettings.Region);
                }

                return client.Build();
            });
        }

        // Register file storage services using keyed services as singleton to match resolver lifetime
        _ = services.AddKeyedSingleton<IFileStorageService, DatabaseStorageService>("Database");

        // Only register MinIO storage service if MinIO client is configured
        if (minioSettings is not null && !string.IsNullOrEmpty(minioSettings.Endpoint))
        {
            _ = services.AddKeyedSingleton<IFileStorageService, MinioStorageService>("Minio");
        }
        //  services.AddKeyedScoped<IFileStorageService, S3StorageService>("S3");

        // Only register S3 if IAmazonS3 is available (to avoid migration issues)
        // This will be registered in the main application startup where AWS services are configured

        // Register the file storage resolver
        _ = services.AddSingleton<IFileStorageResolver, FileStorageResolver>();

        // Add file storage services
        _ = services.AddSingleton<IFileStorageManager, FileStorageManager>();

        // Add database exception analyzer
        _ = services.AddScoped<IDbExceptionAnalyzer, DbExceptionAnalyzer>();

        // Add token usage tracking service
        _ = services.AddScoped<ITokenUsageService, TokenUsageService>();
        _ = services.AddScoped<TokenUsageTotalsSweep>();

        // Add organization model settings service
        _ = services.AddScoped<IOrganizationModelSettingsService, OrganizationModelSettingsService>();

        _ = services.AddScoped<IUserSessionRevoker, UserSessionRevoker>();
        _ = services.AddScoped<ISuperAdminLookup, SuperAdminLookup>();

        // Add soft-delete cleanup service
        _ = services.AddScoped<ISoftDeleteCleanupService, SoftDeleteCleanupService>();
        _ = services.AddKeptTablePurgeSteps();
        _ = services.AddScoped<IWorkflowTypeRetirement, WorkflowTypeRetirement>();

        _ = services.AddScoped<IWorkflowVersionRetentionService, WorkflowVersionRetentionService>();

        services.TryAddTransient<IIntakeRefusalRecorder, LoggingIntakeRefusalRecorder>();
        services.TryAddTransient<IAuthenticationAuditLog, LoggingAuthenticationAuditLog>();
        _ = services.AddTransient<DocumentIntakeGate>();
        services.TryAddTransient<IRunInputStage, PassThroughRunInputStage>();
        services.TryAddTransient<IRunContentCheck, PassRunContentCheck>();

        // Add platform version provider (stamps regression runs with the executing build)
        _ = services.AddSingleton<IPlatformVersionProvider, PlatformVersionProvider>();

        return services;
    }

    /// <summary>
    /// One registration for every host: option binding plus startup validation of
    /// <see cref="AgentRuntimeSettings"/> always, the sidecar HTTP client only when enabled.
    /// Shared so a host cannot wire the client while skipping the validation that makes the
    /// settings' fail-at-startup guarantees real.
    /// </summary>
    public static IServiceCollection AddAgentRuntimeClient(
        this IServiceCollection services,
        PlatformSettings platformSettings)
    {
        return services.AddAgentRuntimeClient(
            platformSettings.AgentRuntime,
            ModelProviders.ForDeploymentMode(platformSettings.DeploymentMode));
    }

    /// <summary>
    /// <see cref="AddAgentRuntimeClient(IServiceCollection, PlatformSettings)"/> for a
    /// deployment whose model provider is already known. <paramref name="modelProvider"/> is
    /// the provider the runtime's model calls go through (<see cref="ModelProviders"/>), or
    /// null for a deployment that declares none; when it is known, an enabled runtime's
    /// allowlist must be of that provider's shape — Bedrock inference-profile ids on AWS and
    /// behind a model gateway, Foundry deployment names on Azure — or startup refuses, and a
    /// provider that names its deployments has those names registered so usage under them is
    /// attributed and priced as that provider.
    /// </summary>
    public static IServiceCollection AddAgentRuntimeClient(
        this IServiceCollection services,
        AgentRuntimeSettings agentRuntimeSettings,
        string? modelProvider)
    {
        if (agentRuntimeSettings.Enabled && modelProvider is not null)
        {
            BindAllowedModelsToProvider(agentRuntimeSettings, modelProvider);
        }

        _ = services.AddOptions<AgentRuntimeSettings>()
            .BindConfiguration(AgentRuntimeSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = services.AddOptions<EditorAgentSettings>()
            .BindConfiguration(EditorAgentSettings.SECTION_NAME)
            .ValidateDataAnnotations();

        _ = services.AddOptions<AgentConversationSettings>()
            .BindConfiguration(AgentConversationSettings.SECTION_NAME)
            .ValidateDataAnnotations();

        _ = services.AddSingleton<IAgentRunActivityFeed, AgentRunActivityFeed>();

        // Conversations resolve on every host, enabled or not: retention keeps deleting after the
        // runtime is turned off, and a turn sent while it is off settles Failed instead of hanging.
        _ = services.AddAgentConversations();

        // The limits provider resolves on every host, enabled or not: save-time validation and
        // the editor inject it unconditionally, and on a disabled host it simply never fills.
        _ = services.AddSingleton<AgentRunLimitsCache>();
        _ = services.AddSingleton<IAgentRunLimitsProvider>(
            static serviceProvider => serviceProvider.GetRequiredService<AgentRunLimitsCache>());
        _ = services.AddSingleton<IAgentRuntimeEnvironmentProvider, AgentRuntimeEnvironmentCache>();

        if (!agentRuntimeSettings.Enabled)
        {
            return services;
        }

        _ = services.AddTransient<AgentRuntimeResilienceHandler>();
        _ = services.AddHttpClient<IAgentRuntimeClient, AgentRuntimeClient>(static (serviceProvider, client) =>
        {
            AgentRuntimeSettings settings =
                serviceProvider.GetRequiredService<IOptions<AgentRuntimeSettings>>().Value;

            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            if (!string.IsNullOrWhiteSpace(settings.AuthToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", settings.AuthToken);
            }
        }).AddHttpMessageHandler<AgentRuntimeResilienceHandler>();

        _ = services.AddTransient<IAgentRunTraceCollector, AgentRunTraceCollector>();
        _ = services.AddTransient<IAgentRunTracePersister, AgentRunTracePersister>();
        _ = services.AddTransient<IAgentRunSessionBundleCustodian, AgentRunSessionBundleCustodian>();
        _ = services.AddTransient<IAgentRunSessionBundleReaper, AgentRunSessionBundleReaper>();
        _ = services.AddSingleton<AgentRunPollGate>();
        _ = services.AddTransient<IAgentRunExecutor, AgentRunExecutor>();
        _ = services.AddTransient<IAgentRunTerminalCleanup, AgentRunTerminalCleanup>();
        _ = services.AddTransient<IAgentConversationRunner, AgentConversationRunner>();
        _ = services.AddHostedService<AgentRunLimitsRefreshService>();

        return services;
    }

    /// <summary>
    /// The shared conversation turn layer without the runner, which needs the runtime client.
    /// Public so a test host can wire the same set.
    /// </summary>
    public static IServiceCollection AddAgentConversations(this IServiceCollection services)
    {
        _ = services.AddSingleton<IAgentConversationEvents, AgentConversationEvents>();
        _ = services.AddSingleton<AgentHostIdentity>();
        _ = services.AddSingleton<AgentConversationPolicies>();
        _ = services.AddSingleton<AgentConversationTurns>();
        _ = services.AddSingleton<IAgentConversationTurns>(
            static serviceProvider => serviceProvider.GetRequiredService<AgentConversationTurns>());
        _ = services.AddTransient<AgentConversationLedger>();
        _ = services.AddTransient<AgentConversationTurnJob>();
        _ = services.AddTransient<IAgentConversationReaper, AgentConversationReaper>();

        return services;
    }

    /// <summary>
    /// The purge steps of the closed modules' tables, which every build keeps under one schema, so
    /// a hard-deleted document loses its excerpts there whether or not the module is installed.
    /// </summary>
    public static IServiceCollection AddKeptTablePurgeSteps(this IServiceCollection services)
    {
        return services
            .AddScoped<IDocumentPurgeStep, ScreeningRecordRetention>()
            .AddScoped<IDocumentPurgeStep, ScreeningBacktestRetention>();
    }

    /// <summary>
    /// Registers the platform's own chat and embeddings connectors for this host's
    /// <see cref="PlatformSettings.DeploymentMode"/>. Both hosts call it, so the mode picks the
    /// connectors in one place; in the gateway mode the closed module that supplies the gateway
    /// registers it and its embeddings connector first.
    /// </summary>
    public static IServiceCollection AddModelConnectors(this IServiceCollection services, PlatformSettings platformSettings)
    {
        return services.AddModelConnectors(platformSettings, platformSettings.DeploymentMode);
    }

    /// <summary>
    /// <see cref="AddModelConnectors(IServiceCollection, PlatformSettings)"/> for a deployment whose mode
    /// is already known.
    /// </summary>
    public static IServiceCollection AddModelConnectors(
        this IServiceCollection services,
        PlatformSettings platformSettings,
        DeploymentMode mode)
    {
        switch (mode)
        {
            case DeploymentMode.Gateway:
                return services.Any(service => service.ServiceType == typeof(IModelGateway))
                    ? services.AddSingleton<IChatConnector, AnthropicChatConnector>()
                    : throw new InvalidOperationException(IModelGateway.NOT_IN_BUILD);
            case DeploymentMode.Azure:
                _ = platformSettings.AzureFoundry.UseEntraId
                    ? services.AddSingleton<IAzureTokenProvider, DefaultAzureTokenProvider>()
                    : services.AddSingleton<IAzureTokenProvider, NullAzureTokenProvider>();

                return services.AddSingleton<IChatConnector, AnthropicChatConnector>()
                    .AddSingleton<IEmbeddingsConnector, AzureCohereEmbeddingsConnector>();
            case DeploymentMode.Aws:
                return services.AddSingleton<IChatConnector, AwsBedrockChatProvider>()
                    .AddSingleton<IEmbeddingsConnector, CohereAwsEmbeddingsConnector>();
            case DeploymentMode.DirectApi:
                return services.AddSingleton<IChatConnector, AnthropicChatConnector>()
                    .AddSingleton<IEmbeddingsConnector, CohereEmbeddingsConnector>();
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown deployment mode.");
        }
    }

    private static void BindAllowedModelsToProvider(AgentRuntimeSettings agentRuntimeSettings, string modelProvider)
    {
        List<string> allowedModels = agentRuntimeSettings.AllowedModels ?? [];

        if (ModelProviders.DescribeAllowlistRefusal(modelProvider, allowedModels) is string refusal)
        {
            throw new InvalidOperationException(
                $"{AgentRuntimeSettings.SectionName}:{nameof(AgentRuntimeSettings.AllowedModels)} mirrors the runtime's "
                + $"allowlist, and this deployment runs its model calls through {modelProvider}: {refusal}.");
        }

        if (ModelProviders.NamesDeployments(modelProvider))
        {
            ModelIdNormalizer.RegisterDeployments(modelProvider, allowedModels);
        }
    }
}
