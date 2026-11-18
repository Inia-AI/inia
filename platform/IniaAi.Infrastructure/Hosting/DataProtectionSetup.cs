using Amazon.SimpleSystemsManagement;
using Azure.Identity;
using IniaAi.Configuration;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.Hosting;

/// <summary>
/// The Data Protection key ring the API and the admin app share. A key the admin app stores decrypts in the API,
/// so both read the ring from the same <c>DataProtection</c> settings and the same application name.
/// </summary>
public static class DataProtectionSetup
{
    private const string APPLICATION_NAME = "IniaAi";
    private const string DEFAULT_AWS_PARAMETER_PATH = "/iniaai/dataprotection/keys";

    /// <summary>
    /// Adds Data Protection with the key storage the <c>DataProtection:Provider</c> setting names: <c>aws</c>
    /// (Parameter Store, encrypted with <c>DataProtection:AwsSsm:KmsKeyId</c> or the default key), <c>azure</c>
    /// (the database, encrypted with the Azure Key Vault key <c>DataProtection:AzureKeyVault:KeyUri</c>) or
    /// <c>database</c>, the default, encrypted with that Key Vault key when it is set.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration that holds the <c>DataProtection</c> section.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddIniaAiDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection dataProtectionSection = configuration.GetSection("DataProtection");
        string provider = dataProtectionSection["Provider"]?.ToLowerInvariant() ?? "database";
        string? azureKeyUri = dataProtectionSection["AzureKeyVault:KeyUri"];

        IDataProtectionBuilder dataProtectionBuilder = services.AddDataProtection()
            .SetApplicationName(APPLICATION_NAME);

        if (provider is "aws" or "awsssm")
        {
            PersistKeysToAwsSsm(services, dataProtectionBuilder, dataProtectionSection["AwsSsm:ParameterPath"], dataProtectionSection["AwsSsm:KmsKeyId"]);
            return services;
        }

        if (provider is "azure" or "azurekeyvault" && string.IsNullOrEmpty(azureKeyUri))
        {
            throw new InvalidOperationException(
                "DataProtection:AzureKeyVault:KeyUri is required when using Azure Key Vault provider. " +
                "Example: https://your-vault.vault.azure.net/keys/DataProtectionKey");
        }

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

        return services;
    }

    private static void PersistKeysToAwsSsm(
        IServiceCollection services,
        IDataProtectionBuilder dataProtectionBuilder,
        string? parameterPath,
        string? kmsKeyId)
    {
        if (string.IsNullOrEmpty(parameterPath))
        {
            parameterPath = DEFAULT_AWS_PARAMETER_PATH;
            Serilog.Log.Information("Using default AWS SSM parameter path: {Path}", parameterPath);
        }

        _ = services.TryAddAWSService<IAmazonSimpleSystemsManagement>();
        _ = dataProtectionBuilder.PersistKeysToAWSSystemsManager(parameterPath, options =>
        {
            if (!string.IsNullOrEmpty(kmsKeyId))
            {
                options.KMSKeyId = kmsKeyId;
            }
        });

        if (!string.IsNullOrEmpty(kmsKeyId))
        {
            Serilog.Log.Information("Data Protection keys will be stored in AWS SSM at {Path} with KMS key {KmsKeyId}",
                parameterPath, kmsKeyId[..Math.Min(20, kmsKeyId.Length)] + "...");
        }
        else
        {
            Serilog.Log.Information("Data Protection keys will be stored in AWS SSM at {Path} (using default SSM encryption)",
                parameterPath);
        }
    }
}
