using Amazon.SimpleSystemsManagement;
using Azure.Identity;
using IniaAi.Configuration;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Services.Jobs;
using IniaAi.WebApi.BackgroundServices;
using IniaAi.WebApi.Configuration;
using IniaAi.WebApi.Extensions;
using IniaAi.WebApi.Filters;
using Microsoft.AspNetCore.DataProtection;

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

        // Configure Data Protection with secure key storage
        ConfigureDataProtection(builder, platformSettings);

        _ = builder.Services.AddMemoryCache();
        _ = builder.Services.AddDistributedMemoryCache();
        _ = builder.Services.AddHttpContextAccessor();

        FileStorageSettings? fileStorageSection = builder.Configuration.GetSection(FileStorageSettings.SECTION_NAME).Get<FileStorageSettings>();

        if (fileStorageSection?.DefaultProvider.Equals("MinIO", StringComparison.OrdinalIgnoreCase) == true)
        {
            _ = services.AddHostedService<FileStorageMigrationService>();
        }

        // Register background services
        _ = services.AddHostedService<ModelPricingSeedingService>();
        _ = services.AddHostedService<SoftDeleteCleanupBackgroundService>();
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

    /// <summary>
    /// Configures Data Protection with secure key storage.
    /// Keys are stored in the database and protected with Azure Key Vault or AWS SSM Parameter Store.
    /// </summary>
    private static void ConfigureDataProtection(WebApplicationBuilder builder, PlatformSettings platformSettings)
    {
        IConfigurationSection dataProtectionSection = builder.Configuration.GetSection("DataProtection");
        string provider = dataProtectionSection["Provider"]?.ToLowerInvariant() ?? "database";
        string? azureKeyUri = dataProtectionSection["AzureKeyVault:KeyUri"];
        string? awsParameterPath = dataProtectionSection["AwsSsm:ParameterPath"];
        string? awsKmsKeyId = dataProtectionSection["AwsSsm:KmsKeyId"];

        IDataProtectionBuilder dataProtectionBuilder = builder.Services.AddDataProtection()
            .SetApplicationName("IniaAi");

        // Configure key storage based on provider
        switch (provider)
        {
            case "aws":
            case "awsssm":
                ConfigureAwsSsmDataProtection(builder, dataProtectionBuilder, awsParameterPath, awsKmsKeyId);
                break;

            case "azure":
            case "azurekeyvault":
                ConfigureAzureKeyVaultDataProtection(dataProtectionBuilder, azureKeyUri);
                break;

            case "database":
            default:
                // Store keys in database with optional Azure Key Vault protection
                _ = dataProtectionBuilder.PersistKeysToDbContext<UserDataDbContext>();

                if (!string.IsNullOrEmpty(azureKeyUri))
                {
                    _ = dataProtectionBuilder.ProtectKeysWithAzureKeyVault(
                        new Uri(azureKeyUri),
                        new DefaultAzureCredential());
                    Serilog.Log.Information("Data Protection keys stored in database, protected with Azure Key Vault");
                }
                else if (!PlatformSettings.IsInDevEnv)
                {
                    Serilog.Log.Warning(
                        "SECURITY WARNING: Data Protection keys are NOT protected with a master key. " +
                        "Configure DataProtection:Provider to 'aws' or 'azure', or set DataProtection:AzureKeyVault:KeyUri. " +
                        "On Linux/containers, keys may be stored unencrypted!");
                }

                break;
        }

        // Set key lifetime (default is 90 days, which is recommended)
        _ = dataProtectionBuilder.SetDefaultKeyLifetime(TimeSpan.FromDays(90));
    }

    /// <summary>
    /// Configures AWS SSM Parameter Store for Data Protection key storage.
    /// Keys are stored in SSM Parameter Store and encrypted with SSM's default encryption or custom KMS key.
    /// </summary>
    private static void ConfigureAwsSsmDataProtection(
        WebApplicationBuilder builder,
        IDataProtectionBuilder dataProtectionBuilder,
        string? parameterPath,
        string? kmsKeyId)
    {
        if (string.IsNullOrEmpty(parameterPath))
        {
            parameterPath = "/iniaai/dataprotection/keys";
            Serilog.Log.Information("Using default AWS SSM parameter path: {Path}", parameterPath);
        }

        // Register AWS SSM client
        _ = builder.Services.AddAWSService<IAmazonSimpleSystemsManagement>();

        // If KMS key ID is provided, use it for encryption
        if (!string.IsNullOrEmpty(kmsKeyId))
        {
            _ = dataProtectionBuilder.PersistKeysToAWSSystemsManager(parameterPath, options =>
            {
                options.KMSKeyId = kmsKeyId;
            });
            Serilog.Log.Information("Data Protection keys will be stored in AWS SSM at {Path} with KMS key {KmsKeyId}",
                parameterPath, kmsKeyId[..Math.Min(20, kmsKeyId.Length)] + "...");
        }
        else
        {
            // Use SSM default encryption (SecureString with AWS-managed key)
            _ = dataProtectionBuilder.PersistKeysToAWSSystemsManager(parameterPath);
            Serilog.Log.Information("Data Protection keys will be stored in AWS SSM at {Path} (using default SSM encryption)",
                parameterPath);
        }
    }

    /// <summary>
    /// Configures Azure Key Vault for Data Protection key storage and protection.
    /// </summary>
    private static void ConfigureAzureKeyVaultDataProtection(
        IDataProtectionBuilder dataProtectionBuilder,
        string? keyUri)
    {
        if (string.IsNullOrEmpty(keyUri))
        {
            throw new InvalidOperationException(
                "DataProtection:AzureKeyVault:KeyUri is required when using Azure Key Vault provider. " +
                "Example: https://your-vault.vault.azure.net/keys/DataProtectionKey");
        }

        // For Azure provider, we use Azure Key Vault for both storage and protection
        _ = dataProtectionBuilder.ProtectKeysWithAzureKeyVault(
            new Uri(keyUri),
            new DefaultAzureCredential());

        Serilog.Log.Information("Data Protection keys protected with Azure Key Vault: {KeyUri}",
            keyUri[..Math.Min(50, keyUri.Length)] + "...");
    }
}
