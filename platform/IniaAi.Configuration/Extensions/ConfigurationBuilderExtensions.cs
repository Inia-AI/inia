using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using IniaAi.Common.Text;
using IniaAi.Configuration.Email;
using IniaAi.Configuration.Platform;
using IniaAi.Configuration.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace IniaAi.Configuration.Extensions;

/// <summary>
/// Provides extension methods for setting up the configuration for the application.
/// </summary>
public static class ConfigurationBuilderExtensions
{
    private const string SCOPE_SHARED = "shared";
    private const string SERVICE_TYPE_DEFAULT = "api";

    private static readonly TimeSpan _secretsManagerTimeLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Names the directories, separated by the platform's path separator, whose files each set one
    /// setting: the file name is the key, with <c>__</c> between sections, and the content is the value.
    /// A container reads its secrets this way, so they stay out of its environment.
    /// </summary>
    public const string SETTINGS_DIRECTORIES = "SETTINGS_DIRECTORIES";

    /// <summary>
    /// Sets up the configuration for the application asynchronously.
    /// <para>
    /// Order matters: appsettings.json -> AWS Secrets Manager/SSM or Azure Key Vault
    /// -> Dev appsettings -> the files of <see cref="SETTINGS_DIRECTORIES"/> -> Environment Variables.
    /// Later sources override earlier sources.
    /// </para>
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to set up.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The <see cref="IConfigurationRoot"/> after the configuration setup is complete.</returns>
    public static async Task<IConfigurationRoot> SetupConfigurationAsync(
        this IConfigurationBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ILogger logger = CreateStartupLogger();

        // Always start with the base JSON
        _ = builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        IConfigurationRoot tempConfig = builder.Build();

        // Service type for shared/service config inheritance: api or admin (from appsettings, defaults to api)
        string serviceType = tempConfig["ServiceType"]?.ToLowerInvariant() ?? SERVICE_TYPE_DEFAULT;

        bool isRunningInAws = PlatformSettings.IsRunningInAws;
        bool isRunningInAzure = PlatformSettings.IsRunningInAzure;

        if (isRunningInAws && isRunningInAzure)
        {
            throw new InvalidOperationException(
                $"AWS and Azure modes are both active — set exactly one of {CloudHost.FORCE_AWS_MODE} and " +
                $"{CloudHost.FORCE_AZURE_MODE}.");
        }

        // Load AWS Secrets Manager and SSM parameters
        if (isRunningInAws)
        {
            _ = builder.AddJsonFile("appsettings.Aws.json", optional: false, reloadOnChange: true);

            // Get environment for path-based secrets (dev, staging, prod)
            // PlatformSettings.CurrentEnvironment already maps to abbreviated names
            string currentEnvironment = PlatformSettings.CurrentEnvironment;

            string ssmSharedPath = $"/iniaai/{currentEnvironment}/{SCOPE_SHARED}";
            string ssmServicePath = $"/iniaai/{currentEnvironment}/{serviceType}";

            logger.LogInformation(
                "AWS Config: Loading from environment '{Environment}', service '{Service}' " +
                "(SSM shared: {SharedPath}, SSM service: {ServicePath})",
                currentEnvironment, serviceType, ssmSharedPath, ssmServicePath);

            // AWS Secrets Manager for critical secrets (JWT keys, DB connections)
            // Uses same pattern as SSM: shared/secrets/* and {service}/secrets/*
            Dictionary<string, string?> secretsManagerConfig = await LoadSecretsManagerSecretsAsync(
                logger,
                currentEnvironment,
                serviceType,
                cancellationToken).ConfigureAwait(false);
            _ = builder.AddInMemoryCollection(secretsManagerConfig);

            // SSM Parameter Store Inheritance Pattern (FREE with SecureString):
            // Access Control: config/ = String (visible to all), secrets/ = SecureString (restricted)
            // 1. Load shared config: /iniaai/{env}/shared/config/*
            // 2. Load shared secrets: /iniaai/{env}/shared/secrets/*
            // 3. Load service-specific config: /iniaai/{env}/{service}/config/*
            // 4. Load service-specific secrets: /iniaai/{env}/{service}/secrets/*
            // Later values override earlier values.

            // Step 1: Load shared config (base non-sensitive configuration)
            _ = builder.AddSystemsManager(source =>
            {
                source.Path = $"{ssmSharedPath}/config";
                source.Optional = true;
                source.ReloadAfter = TimeSpan.FromMinutes(5);
            });

            // Step 2: Load shared secrets (base sensitive configuration)
            _ = builder.AddSystemsManager(source =>
            {
                source.Path = $"{ssmSharedPath}/secrets";
                source.Optional = true;
                source.ReloadAfter = TimeSpan.FromMinutes(5);
            });

            // Step 3: Load service-specific config (overrides shared config)
            _ = builder.AddSystemsManager(source =>
            {
                source.Path = $"{ssmServicePath}/config";
                source.Optional = true;
                source.ReloadAfter = TimeSpan.FromMinutes(5);
            });

            // Step 4: Load service-specific secrets (overrides shared secrets)
            _ = builder.AddSystemsManager(source =>
            {
                source.Path = $"{ssmServicePath}/secrets";
                source.Optional = true;
                source.ReloadAfter = TimeSpan.FromMinutes(5);
            });
        }

        // Load Azure Key Vault secrets
        if (isRunningInAzure)
        {
            _ = builder.AddJsonFile("appsettings.Azure.json", optional: false, reloadOnChange: true);

            string? keyVaultUri = Environment.GetEnvironmentVariable("AZURE_KEY_VAULT_URI");

            if (string.IsNullOrEmpty(keyVaultUri))
            {
                logger.LogWarning(
                    "Azure Config: AZURE_KEY_VAULT_URI is not set — skipping Key Vault. " +
                    "Configuration must come from appsettings and environment variables.");
            }
            else
            {
                if (!Uri.TryCreate(keyVaultUri, UriKind.Absolute, out Uri? vaultUri))
                {
                    throw new InvalidOperationException(
                        $"AZURE_KEY_VAULT_URI '{keyVaultUri}' is not a valid absolute URI — " +
                        "expected the vault URI, e.g. https://<vault-name>.vault.azure.net/");
                }

                logger.LogInformation(
                    "Azure Config: Loading Key Vault '{KeyVaultUri}' secrets for service '{Service}' " +
                    "(shared--* first, then service-specific overrides)",
                    keyVaultUri, serviceType);

                var secretClient = new SecretClient(vaultUri, new DefaultAzureCredential());

                // One LIST page proves identity, RBAC, and DNS now, so a set-but-unreachable vault
                // stops startup with a named cause instead of failing later inside builder.Build().
                try
                {
                    _ = await secretClient.GetPropertiesOfSecretsAsync(cancellationToken)
                        .AsPages()
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException(
                        $"AZURE_KEY_VAULT_URI '{keyVaultUri}' is unreachable. " +
                        "Check the instance's managed identity, its 'Key Vault Secrets User' role " +
                        "assignment on the vault, and private-endpoint DNS resolution.",
                        ex);
                }

                // Key Vault Inheritance Pattern (secret names, since vaults have no paths):
                // 1. Load shared secrets: shared--*
                // 2. Load service-specific secrets: {service}--* (overrides shared)
                // Managers strip the prefix and map '--' to ':' config keys.
                _ = builder.AddAzureKeyVault(secretClient, new AzureKeyVaultConfigurationOptions
                {
                    Manager = new PrefixKeyVaultSecretManager(SCOPE_SHARED),
                    ReloadInterval = TimeSpan.FromMinutes(5),
                });

                _ = builder.AddAzureKeyVault(secretClient, new AzureKeyVaultConfigurationOptions
                {
                    Manager = new PrefixKeyVaultSecretManager(serviceType),
                    ReloadInterval = TimeSpan.FromMinutes(5),
                });
            }
        }

        // Load dev env name (optional)
        string? environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (!string.IsNullOrEmpty(environment))
        {
            _ = builder.AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true);
        }

        foreach (string directory in SettingsDirectories())
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"{SETTINGS_DIRECTORIES} names '{directory}', which does not exist.");
            }

            _ = builder.Add(new SettingsDirectoryConfigurationSource(directory));
        }

        // Env vars always last
        _ = builder.AddEnvironmentVariables();

        // FINAL build now that all sources are known
        IConfigurationRoot configuration = builder.Build();

        // Setup logger with or without CloudWatch
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        Log.Logger.Information(
            "\n==================================================================\n" +
            "Starting application.\n" +
            "==================================================================");

        logger.LogInformation("{Settings}", DescribeStartupSettings(configuration));

        if (PlatformSettings.IsInDevEnv)
        {
            LogConnectionStrings(configuration, logger);
        }

        logger.LogInformation("Configuration setup complete.");

        return configuration;
    }

    private static string[] SettingsDirectories()
    {
        return Environment.GetEnvironmentVariable(SETTINGS_DIRECTORIES)?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    }

    private static void LogConnectionStrings(
        IConfigurationRoot configuration,
        ILogger logger)
    {
        var settingsBuilder = new StringBuilder();

        _ = settingsBuilder.AppendLine("\n==================================================================");
        _ = settingsBuilder.AppendLine("Connection Strings:");
        _ = settingsBuilder.AppendLine("==================================================================");

        foreach (IConfigurationSection connString in configuration.GetSection("ConnectionStrings").GetChildren())
        {
            string[]? conStrParts = connString.Value?.Split(';');
            string conStrToLog = conStrParts is not null && conStrParts.Any(p => p.Contains("Application Name="))
                ? conStrParts.FirstOrDefault(p => p.Contains("Application Name="))
                    ?? SecretMasker.Mask(connString.Value)
                : SecretMasker.Mask(connString.Value);

            _ = settingsBuilder.AppendLine($"  {connString.Key}: {conStrToLog}");
        }

        _ = settingsBuilder.AppendLine("==================================================================");

        logger.LogInformation("{Settings}", settingsBuilder.ToString());
    }

    public static ILogger CreateStartupLogger()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            _ = builder.AddSimpleConsole();
        });

        return loggerFactory.CreateLogger("ConfigurationBuilder");
    }

    /// <summary>
    /// The configuration provider that supplied a key's value, as the startup settings log prints it, such as <c>[env]</c>.
    /// </summary>
    public static string SourceTag(IConfigurationRoot configuration, string key)
    {
        return $"[{GetConfigSource(configuration, key)}]";
    }

    /// <summary>
    /// The settings block the application logs at startup, bound from the same sections the
    /// application's own readers bind.
    /// </summary>
    internal static string DescribeStartupSettings(IConfigurationRoot configuration)
    {
        PlatformSettings platformSettings = configuration.GetSection("PlatformSettings").Get<PlatformSettings>()
            ?? throw new InvalidOperationException("PlatformSettings not found in configuration.");

        return DescribePlatformSettings(
            configuration,
            platformSettings,
            configuration.GetSection(SendGridSettings.SECTION_NAME).Get<SendGridSettings>(),
            configuration.GetSection(FileStorageSettings.SECTION_NAME).Get<FileStorageSettings>(),
            configuration.GetSection(SmtpSettings.SECTION_NAME).Get<SmtpSettings>());
    }

    internal static string DescribePlatformSettings(
        IConfigurationRoot configuration,
        PlatformSettings platformSettings,
        SendGridSettings? sendGridSettings,
        FileStorageSettings? fileStorageSettings,
        SmtpSettings? smtpSettings)
    {
        // Build a single string with all settings
        var settingsBuilder = new StringBuilder();

        // Helper: resolve which config provider supplied a value
        string Src(string key)
        {
            return SourceTag(configuration, key);
        }

        const string ps = "PlatformSettings";

        _ = settingsBuilder.AppendLine("\n==================================================================");
        _ = settingsBuilder.AppendLine("Platform Settings:");
        _ = settingsBuilder.AppendLine("==================================================================");

        // IniaAi Settings
        string inia = $"{ps}:IniaAi";
        _ = settingsBuilder.AppendLine("IniaAi Settings:");
        _ = settingsBuilder.AppendLine($"  MaxFileSizeMb: {platformSettings.IniaAi.MaxFileSizeMb} {Src($"{inia}:MaxFileSizeMb")}");
        _ = settingsBuilder.AppendLine($"  BatchSizeForEmbeddings: {platformSettings.IniaAi.BatchSizeForEmbeddings} {Src($"{inia}:BatchSizeForEmbeddings")}");
        _ = settingsBuilder.AppendLine($"  LlmMaxDegreeOfParallelism: {platformSettings.IniaAi.LlmMaxDegreeOfParallelism} {Src($"{inia}:LlmMaxDegreeOfParallelism")}");
        _ = settingsBuilder.AppendLine($"  SummarizationModelType: {platformSettings.IniaAi.SummarizationModelType} {Src($"{inia}:SummarizationModelType")}");
        _ = settingsBuilder.AppendLine($"  NoDataForPlaceHolderText: {platformSettings.IniaAi.NoDataForPlaceHolderText} {Src($"{inia}:NoDataForPlaceHolderText")}");
        _ = settingsBuilder.AppendLine($"  MaxConcurrentJobs: {platformSettings.IniaAi.MaxConcurrentJobs} {Src($"{inia}:MaxConcurrentJobs")}");
        _ = settingsBuilder.AppendLine($"  MaxRetriesForTopicsGeneration: {platformSettings.IniaAi.MaxRetriesForTopicsGeneration} {Src($"{inia}:MaxRetriesForTopicsGeneration")}");
        _ = settingsBuilder.AppendLine($"  WordCountPerPage: {platformSettings.IniaAi.WordCountPerPage} {Src($"{inia}:WordCountPerPage")}");

        // Anthropic Settings
        string ant = $"{ps}:Anthropic";
        _ = settingsBuilder.AppendLine("\nAnthropic Settings:");
        _ = settingsBuilder.AppendLine($"  ApiKey: {SecretMasker.Mask(platformSettings.Anthropic.ApiKey)} {Src($"{ant}:ApiKey")}");
        _ = settingsBuilder.AppendLine($"  ApiUrl: {platformSettings.Anthropic.ApiUrl} {Src($"{ant}:ApiUrl")}");
        _ = settingsBuilder.AppendLine($"  ApiVersion: {platformSettings.Anthropic.ApiVersion} {Src($"{ant}:ApiVersion")}");
        _ = settingsBuilder.AppendLine($"  AdvancedModel: {platformSettings.Anthropic.AdvancedModel} {Src($"{ant}:AdvancedModel")}");
        _ = settingsBuilder.AppendLine($"  AdvancedModelIdOverride: {platformSettings.Anthropic.AdvancedModelIdOverride} {Src($"{ant}:AdvancedModelIdOverride")}");
        _ = settingsBuilder.AppendLine($"  AdvancedModelTemperature: {platformSettings.Anthropic.AdvancedModelTemperature} {Src($"{ant}:AdvancedModelTemperature")}");
        _ = settingsBuilder.AppendLine($"  AdvancedModelMaxTokens: {platformSettings.Anthropic.AdvancedModelMaxTokens} {Src($"{ant}:AdvancedModelMaxTokens")}");
        _ = settingsBuilder.AppendLine($"  BasicModel: {platformSettings.Anthropic.BasicModel} {Src($"{ant}:BasicModel")}");
        _ = settingsBuilder.AppendLine($"  BasicModelIdOverride: {platformSettings.Anthropic.BasicModelIdOverride} {Src($"{ant}:BasicModelIdOverride")}");
        _ = settingsBuilder.AppendLine($"  BasicModelTemperature: {platformSettings.Anthropic.BasicModelTemperature} {Src($"{ant}:BasicModelTemperature")}");
        _ = settingsBuilder.AppendLine($"  BasicModelMaxTokens: {platformSettings.Anthropic.BasicModelMaxTokens} {Src($"{ant}:BasicModelMaxTokens")}");
        _ = settingsBuilder.AppendLine($"  MaxRateLimitRetryAttempts: {platformSettings.Anthropic.MaxRateLimitRetryAttempts} {Src($"{ant}:MaxRateLimitRetryAttempts")}");
        _ = settingsBuilder.AppendLine($"  MaxServiceUnavailableRetryAttempts: {platformSettings.Anthropic.MaxServiceUnavailableRetryAttempts} {Src($"{ant}:MaxServiceUnavailableRetryAttempts")}");
        _ = settingsBuilder.AppendLine($"  MaxParallelCalls: {platformSettings.Anthropic.MaxParallelCalls} {Src($"{ant}:MaxParallelCalls")}");
        _ = settingsBuilder.AppendLine($"  MaxInputTokensPerWindow: {platformSettings.Anthropic.MaxInputTokensPerWindow} {Src($"{ant}:MaxInputTokensPerWindow")}");
        _ = settingsBuilder.AppendLine($"  HttpTimeoutMinutes: {platformSettings.Anthropic.HttpTimeoutMinutes} {Src($"{ant}:HttpTimeoutMinutes")}");

        // Cohere Settings
        string coh = $"{ps}:Cohere";
        _ = settingsBuilder.AppendLine("\nCohere Settings:");
        _ = settingsBuilder.AppendLine($"  ApiKey: {SecretMasker.Mask(platformSettings.Cohere.ApiKey)} {Src($"{coh}:ApiKey")}");
        _ = settingsBuilder.AppendLine($"  EmbeddingsApiUrl: {platformSettings.Cohere.EmbeddingsApiUrl} {Src($"{coh}:EmbeddingsApiUrl")}");
        _ = settingsBuilder.AppendLine($"  MaxRateLimitRetryAttempts: {platformSettings.Cohere.MaxRateLimitRetryAttempts} {Src($"{coh}:MaxRateLimitRetryAttempts")}");
        _ = settingsBuilder.AppendLine($"  MaxServiceUnavailableRetryAttempts: {platformSettings.Cohere.MaxServiceUnavailableRetryAttempts} {Src($"{coh}:MaxServiceUnavailableRetryAttempts")}");
        _ = settingsBuilder.AppendLine($"  EmbeddingsModel: {platformSettings.Cohere.EmbeddingsModel} {Src($"{coh}:EmbeddingsModel")}");
        _ = settingsBuilder.AppendLine($"  EmbeddingsOutputDimension: {platformSettings.Cohere.EmbeddingsOutputDimension} {Src($"{coh}:EmbeddingsOutputDimension")}");

        // Azure Foundry Settings
        string azf = $"{ps}:AzureFoundry";
        _ = settingsBuilder.AppendLine("\nAzure Foundry Settings:");
        _ = settingsBuilder.AppendLine($"  AnthropicApiUrl: {platformSettings.AzureFoundry.AnthropicApiUrl} {Src($"{azf}:AnthropicApiUrl")}");
        _ = settingsBuilder.AppendLine($"  AnthropicApiKey: {SecretMasker.Mask(platformSettings.AzureFoundry.AnthropicApiKey)} {Src($"{azf}:AnthropicApiKey")}");
        _ = settingsBuilder.AppendLine($"  UseEntraId: {platformSettings.AzureFoundry.UseEntraId} {Src($"{azf}:UseEntraId")}");
        _ = settingsBuilder.AppendLine($"  CohereApiUrl: {platformSettings.AzureFoundry.CohereApiUrl} {Src($"{azf}:CohereApiUrl")}");
        _ = settingsBuilder.AppendLine($"  CohereApiKey: {SecretMasker.Mask(platformSettings.AzureFoundry.CohereApiKey)} {Src($"{azf}:CohereApiKey")}");
        _ = settingsBuilder.AppendLine($"  CohereDeploymentName: {platformSettings.AzureFoundry.CohereDeploymentName} {Src($"{azf}:CohereDeploymentName")}");

        // Agent Runtime Settings
        string art = AgentRuntimeSettings.SectionName;
        List<string> allowedModels = platformSettings.AgentRuntime.AllowedModels ?? [];
        string allowedModelsText = allowedModels.Count == 0 ? "(none)" : string.Join(", ", allowedModels);
        _ = settingsBuilder.AppendLine("\nAgent Runtime Settings:");
        _ = settingsBuilder.AppendLine($"  Enabled: {platformSettings.AgentRuntime.Enabled} {Src($"{art}:Enabled")}");
        _ = settingsBuilder.AppendLine($"  BaseUrl: {platformSettings.AgentRuntime.BaseUrl} {Src($"{art}:BaseUrl")}");
        // A list has no value of its own; its first entry carries the source.
        _ = settingsBuilder.AppendLine($"  AllowedModels: {allowedModelsText} {Src($"{art}:AllowedModels:0")}");
        _ = settingsBuilder.AppendLine($"  AuthToken: {SecretMasker.Mask(platformSettings.AgentRuntime.AuthToken)} {Src($"{art}:AuthToken")}");

        // GitHub Settings
        string gh = $"{ps}:GitHub";
        _ = settingsBuilder.AppendLine("\nGitHub Settings:");
        _ = settingsBuilder.AppendLine($"  Enabled: {platformSettings.GitHub.Enabled} {Src($"{gh}:Enabled")}");
        _ = settingsBuilder.AppendLine($"  Owner: {platformSettings.GitHub.Owner} {Src($"{gh}:Owner")}");
        _ = settingsBuilder.AppendLine($"  Repo: {platformSettings.GitHub.Repo} {Src($"{gh}:Repo")}");
        _ = settingsBuilder.AppendLine($"  DefaultBranch: {platformSettings.GitHub.DefaultBranch} {Src($"{gh}:DefaultBranch")}");
        _ = settingsBuilder.AppendLine($"  Pat: {SecretMasker.Mask(platformSettings.GitHub.Pat)} {Src($"{gh}:Pat")}");

        // MessageProcessor Settings
        string mp = $"{ps}:MessageProcessor";
        _ = settingsBuilder.AppendLine("\nMessageProcessor Settings:");
        _ = settingsBuilder.AppendLine($"  ApiKey: {SecretMasker.Mask(platformSettings.MessageProcessor.ApiKey)} {Src($"{mp}:ApiKey")}");

        // Exa Settings
        string exa = $"{ps}:Exa";
        _ = settingsBuilder.AppendLine("\nExa Settings:");
        _ = settingsBuilder.AppendLine($"  ExaApiUrl: {platformSettings.Exa.ExaApiUrl} {Src($"{exa}:ExaApiUrl")}");
        _ = settingsBuilder.AppendLine($"  ApiKey: {SecretMasker.Mask(platformSettings.Exa.ApiKey)} {Src($"{exa}:ApiKey")}");
        _ = settingsBuilder.AppendLine($"  TimeoutMinutes: {platformSettings.Exa.TimeoutMinutes} {Src($"{exa}:TimeoutMinutes")}");
        _ = settingsBuilder.AppendLine($"  MaxRetries: {platformSettings.Exa.MaxRetries} {Src($"{exa}:MaxRetries")}");

        // ExchangeRateApi Settings
        string exr = $"{ps}:ExchangeRateApi";
        _ = settingsBuilder.AppendLine("\nExchangeRateApi Settings:");
        _ = settingsBuilder.AppendLine($"  ApiKey: {SecretMasker.Mask(platformSettings.ExchangeRateApi.ApiKey)} {Src($"{exr}:ApiKey")}");
        _ = settingsBuilder.AppendLine($"  BaseUrl: {platformSettings.ExchangeRateApi.BaseUrl} {Src($"{exr}:BaseUrl")}");
        _ = settingsBuilder.AppendLine($"  MaxRetryAttempts: {platformSettings.ExchangeRateApi.MaxRetryAttempts} {Src($"{exr}:MaxRetryAttempts")}");
        _ = settingsBuilder.AppendLine($"  RetryDelayMilliseconds: {platformSettings.ExchangeRateApi.RetryDelayMilliseconds} {Src($"{exr}:RetryDelayMilliseconds")}");
        _ = settingsBuilder.AppendLine($"  CacheExpirationMinutes: {platformSettings.ExchangeRateApi.CacheExpirationMinutes} {Src($"{exr}:CacheExpirationMinutes")}");

        // OCR Settings
        string ocr = $"{ps}:OcrSettings";
        _ = settingsBuilder.AppendLine("\nOCR Settings:");
        _ = settingsBuilder.AppendLine($"  OcrMaxDegreeOfParallelism: {platformSettings.OcrSettings.OcrMaxDegreeOfParallelism} {Src($"{ocr}:OcrMaxDegreeOfParallelism")}");
        _ = settingsBuilder.AppendLine($"  LlmMaxDegreeOfParallelism: {platformSettings.OcrSettings.LlmMaxDegreeOfParallelism} {Src($"{ocr}:LlmMaxDegreeOfParallelism")}");
        _ = settingsBuilder.AppendLine($"  OcrMaxOutputTokens: {platformSettings.OcrSettings.OcrMaxOutputTokens} {Src($"{ocr}:OcrMaxOutputTokens")}");
        _ = settingsBuilder.AppendLine($"  ShouldUseAdvancedModel: {platformSettings.OcrSettings.ShouldUseAdvancedModel} {Src($"{ocr}:ShouldUseAdvancedModel")}");
        _ = settingsBuilder.AppendLine($"  ModelIdOverride: {platformSettings.OcrSettings.ModelIdOverride} {Src($"{ocr}:ModelIdOverride")}");

        // ChartRendering Settings
        string chr = $"{ps}:ChartRendering";
        _ = settingsBuilder.AppendLine("\nChartRendering Settings:");
        _ = settingsBuilder.AppendLine($"  RenderTimeoutMs: {platformSettings.ChartRendering.RenderTimeoutMs} {Src($"{chr}:RenderTimeoutMs")}");
        _ = settingsBuilder.AppendLine($"  TempDirectory: {platformSettings.ChartRendering.TempDirectory} {Src($"{chr}:TempDirectory")}");
        _ = settingsBuilder.AppendLine($"  DebugPreserveTempFiles: {platformSettings.ChartRendering.DebugPreserveTempFiles} {Src($"{chr}:DebugPreserveTempFiles")}");

        // PromptTemplates Settings
        string pt = $"{ps}:PromptTemplates";
        _ = settingsBuilder.AppendLine("\nPromptTemplates Settings:");
        _ = settingsBuilder.AppendLine($"  UserPromptPreviewSystemPrompt: {MaskLongString(platformSettings.PromptTemplates.UserPromptPreviewSystemPrompt)} {Src($"{pt}:UserPromptPreviewSystemPrompt")}");

        // ModelRouting Settings
        string mr = $"{ps}:ModelRouting";
        _ = settingsBuilder.AppendLine("\nModelRouting Settings:");
        _ = settingsBuilder.AppendLine($"  DefaultModelFamily: {platformSettings.ModelRouting.DefaultModelFamily} {Src($"{mr}:DefaultModelFamily")}");
        _ = settingsBuilder.AppendLine($"  TenantDefaults: {(platformSettings.ModelRouting.TenantDefaults.Count > 0 ? string.Join(", ", platformSettings.ModelRouting.TenantDefaults.Select(kvp => $"{kvp.Key}={kvp.Value}")) : "(none)")}");
        _ = settingsBuilder.AppendLine($"  FunctionOverrides: {(platformSettings.ModelRouting.FunctionOverrides.Count > 0 ? string.Join("; ", platformSettings.ModelRouting.FunctionOverrides.Select(kvp => $"{kvp.Key}: [{string.Join(", ", kvp.Value.Select(f => $"{f.Key}={f.Value}"))}]")) : "(none)")}");
        _ = settingsBuilder.AppendLine($"  TenantModelOverrides: {(platformSettings.ModelRouting.TenantModelOverrides.Count > 0 ? string.Join("; ", platformSettings.ModelRouting.TenantModelOverrides.Select(kvp => $"{kvp.Key}: [Advanced={kvp.Value.AdvancedModel ?? "(none)"}, Basic={kvp.Value.BasicModel ?? "(none)"}, AgentAdvanced={kvp.Value.AgentAdvancedModel ?? "(none)"}, AgentBasic={kvp.Value.AgentBasicModel ?? "(none)"}]")) : "(none)")}");

        // SendGrid Settings
        _ = settingsBuilder.AppendLine("\nSendGrid Settings:");
        _ = settingsBuilder.AppendLine($"  ApiKey: {SecretMasker.Mask(sendGridSettings?.ApiKey)} {Src("SendGrid:ApiKey")}");
        _ = settingsBuilder.AppendLine($"  FromEmail: {sendGridSettings?.FromEmail} {Src("SendGrid:FromEmail")}");
        _ = settingsBuilder.AppendLine($"  FromName: {sendGridSettings?.FromName} {Src("SendGrid:FromName")}");

        // Smtp Settings
        _ = settingsBuilder.AppendLine("\nSmtp Settings:");
        _ = settingsBuilder.AppendLine($"  Host: {smtpSettings?.Host} {Src("Smtp:Host")}");
        _ = settingsBuilder.AppendLine($"  Port: {smtpSettings?.Port} {Src("Smtp:Port")}");
        _ = settingsBuilder.AppendLine($"  EnableSsl: {smtpSettings?.EnableSsl} {Src("Smtp:EnableSsl")}");
        _ = settingsBuilder.AppendLine($"  Username: {smtpSettings?.Username} {Src("Smtp:Username")}");
        _ = settingsBuilder.AppendLine($"  Password: {SecretMasker.Mask(smtpSettings?.Password)} {Src("Smtp:Password")}");
        _ = settingsBuilder.AppendLine($"  FromEmail: {smtpSettings?.FromEmail} {Src("Smtp:FromEmail")}");
        _ = settingsBuilder.AppendLine($"  FromName: {smtpSettings?.FromName} {Src("Smtp:FromName")}");

        // FileStorage Settings
        _ = settingsBuilder.AppendLine("\nFileStorage Settings:");
        string fileStorage = FileStorageSettings.SECTION_NAME;
        _ = settingsBuilder.AppendLine($"  DefaultProvider: {fileStorageSettings?.DefaultProvider} {Src($"{fileStorage}:DefaultProvider")}");
        _ = settingsBuilder.AppendLine($"  StorageBuckets: {(fileStorageSettings?.StorageBuckets?.Count > 0 ? string.Join(", ", fileStorageSettings.StorageBuckets.Select(kvp => $"{kvp.Key}={kvp.Value}")) : "(none)")}");
        _ = settingsBuilder.AppendLine($"  TenantOverrides: {(fileStorageSettings?.TenantOverrides?.Count > 0 ? string.Join(", ", fileStorageSettings.TenantOverrides.Select(kvp => $"{kvp.Key}={kvp.Value}")) : "(none)")}");
        if (fileStorageSettings?.MinIO is not null)
        {
            _ = settingsBuilder.AppendLine("  MinIO:");
            _ = settingsBuilder.AppendLine($"    Endpoint: {fileStorageSettings.MinIO.Endpoint} {Src($"{fileStorage}:MinIO:Endpoint")}");
            _ = settingsBuilder.AppendLine($"    AccessKey: {SecretMasker.Mask(fileStorageSettings.MinIO.AccessKey)} {Src($"{fileStorage}:MinIO:AccessKey")}");
            _ = settingsBuilder.AppendLine($"    SecretKey: {SecretMasker.Mask(fileStorageSettings.MinIO.SecretKey)} {Src($"{fileStorage}:MinIO:SecretKey")}");
            _ = settingsBuilder.AppendLine($"    UseSSL: {fileStorageSettings.MinIO.UseSSL} {Src($"{fileStorage}:MinIO:UseSSL")}");
            _ = settingsBuilder.AppendLine($"    Region: {fileStorageSettings.MinIO.Region} {Src($"{fileStorage}:MinIO:Region")}");
        }

        if (fileStorageSettings?.S3 is not null)
        {
            _ = settingsBuilder.AppendLine("  S3:");
            _ = settingsBuilder.AppendLine($"    Region: {fileStorageSettings.S3.Region} {Src($"{fileStorage}:S3:Region")}");
            _ = settingsBuilder.AppendLine($"    AccessKeyId: {SecretMasker.Mask(fileStorageSettings.S3.AccessKeyId)} {Src($"{fileStorage}:S3:AccessKeyId")}");
            _ = settingsBuilder.AppendLine($"    SecretAccessKey: {SecretMasker.Mask(fileStorageSettings.S3.SecretAccessKey)} {Src($"{fileStorage}:S3:SecretAccessKey")}");
            _ = settingsBuilder.AppendLine($"    ServiceUrl: {fileStorageSettings.S3.ServiceUrl} {Src($"{fileStorage}:S3:ServiceUrl")}");
        }

        // Database Provider Settings
        _ = settingsBuilder.AppendLine("\nDatabase Settings:");
        _ = settingsBuilder.AppendLine($"  UserDataDbProvider: {platformSettings.UserDataDbProvider} {Src($"{ps}:UserDataDbProvider")}");
        _ = settingsBuilder.AppendLine($"  CoreDbProvider: {platformSettings.CoreDbProvider} {Src($"{ps}:CoreDbProvider")}");

        // General Platform Settings (static properties — no config source)
        _ = settingsBuilder.AppendLine("\nGeneral Platform Settings:");
        _ = settingsBuilder.AppendLine($"  IsInDevEnv: {PlatformSettings.IsInDevEnv}");
        _ = settingsBuilder.AppendLine($"  IsInLocalDevEnv: {PlatformSettings.IsInLocalDevEnv}");
        _ = settingsBuilder.AppendLine($"  IsRunningInAws: {PlatformSettings.IsRunningInAws}");
        _ = settingsBuilder.AppendLine($"  IsRunningInAzure: {PlatformSettings.IsRunningInAzure}");
        _ = settingsBuilder.AppendLine($"  ShouldReseedDbs: {PlatformSettings.ShouldReseedDbs}");
        _ = settingsBuilder.AppendLine($"  DeploymentMode: {platformSettings.DeploymentMode}");
        _ = settingsBuilder.AppendLine($"  MaxBlockParallelism: {platformSettings.MaxBlockParallelism} {Src($"{ps}:MaxBlockParallelism")}");
        _ = settingsBuilder.AppendLine($"  CustomerOrgId: {platformSettings.CustomerOrgId} {Src($"{ps}:CustomerOrgId")}");
        _ = settingsBuilder.AppendLine($"  InstanceName: {platformSettings.InstanceName} {Src($"{ps}:InstanceName")}");

        _ = settingsBuilder.AppendLine("==================================================================");

        return settingsBuilder.ToString();
    }

    /// <summary>
    /// Resolves which configuration provider supplied the value for a given key.
    /// Iterates providers in reverse order (last wins) to find the winning source.
    /// </summary>
    private static string GetConfigSource(IConfigurationRoot configuration, string key)
    {
        // Walk providers in reverse — last one with a value is the winner
        foreach (IConfigurationProvider provider in configuration.Providers.Reverse())
        {
            if (!provider.TryGet(key, out _))
            {
                continue;
            }

            string providerName = provider.ToString() ?? provider.GetType().Name;

            // Extract a short, readable source name
            if (providerName.Contains("JsonConfigurationProvider", StringComparison.Ordinal))
            {
                // "JsonConfigurationProvider for 'appsettings.json' (Optional)" → "appsettings.json"
                int start = providerName.IndexOf('\'');
                int end = providerName.LastIndexOf('\'');
                return start >= 0 && end > start
                    ? providerName[(start + 1)..end]
                    : "json";
            }

            return providerName.Contains("EnvironmentVariables", StringComparison.Ordinal)
                ? "env"
                : providerName.Contains("KeyPerFile", StringComparison.Ordinal)
                ? "file"
                : providerName.Contains("SystemsManager", StringComparison.Ordinal)
                ? "ssm"
                : providerName.Contains("KeyVault", StringComparison.Ordinal)
                ? "key-vault"
                : providerName.Contains("Memory", StringComparison.Ordinal) ? "secrets-manager" : providerName;
        }

        return "default";
    }

    /// <summary>
    /// Masks a long string by showing only a preview with character count
    /// </summary>
    private static string MaskLongString(string? value, int previewLength = 50)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(empty)";
        }

        if (value.Length <= previewLength)
        {
            return $"\"{value}\"";
        }

        string preview = value[..previewLength].Replace("\n", "\\n").Replace("\r", "");
        return $"\"{preview}...\" ({value.Length} chars)";
    }

    /// <summary>
    /// Loads secrets from AWS Secrets Manager dynamically.
    /// Uses same pattern as SSM: iniaai/{env}/shared/secrets/* and iniaai/{env}/{service}/secrets/*
    /// Secret path after 'secrets/' maps directly to config key (/ → :)
    /// </summary>
    private static async Task<Dictionary<string, string?>> LoadSecretsManagerSecretsAsync(
        ILogger logger,
        string environment,
        string serviceType,
        CancellationToken cancellationToken = default)
    {
        using var client = new AmazonSecretsManagerClient();
        using var timeLimit = new CancellationTokenSource(_secretsManagerTimeLimit);

        return await LoadSecretsManagerSecretsAsync(client, logger, environment, serviceType, timeLimit.Token, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<Dictionary<string, string?>> LoadSecretsManagerSecretsAsync(
        IAmazonSecretsManager client,
        ILogger logger,
        string environment,
        string serviceType,
        CancellationToken timeLimitReached,
        CancellationToken cancellationToken)
    {
        var secrets = new Dictionary<string, string?>();

        // Load in order: shared secrets first, then service-specific (overrides shared)
        string[] prefixes = new[]
        {
            $"iniaai/{environment}/{SCOPE_SHARED}/secrets/",
            $"iniaai/{environment}/{serviceType}/secrets/",
        };

        using var stopLoading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeLimitReached);

        foreach (string? prefix in prefixes)
        {
            int secretsLoadedFromPrefix = 0;

            try
            {
                // List all secrets with this prefix (with pagination)
                var listRequest = new ListSecretsRequest
                {
                    Filters = [new Filter { Key = FilterNameStringType.Name, Values = [prefix] }]
                };

                do
                {
                    ListSecretsResponse listResponse = await client.ListSecretsAsync(listRequest, stopLoading.Token)
                        .ConfigureAwait(false);

                    foreach (SecretListEntry? secretEntry in listResponse.SecretList)
                    {
                        if (!secretEntry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try
                        {
                            var getRequest = new GetSecretValueRequest { SecretId = secretEntry.Name };
                            GetSecretValueResponse getResponse = await client.GetSecretValueAsync(getRequest, stopLoading.Token)
                                .ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(getResponse.SecretString))
                            {
                                // Strip prefix and convert path to config key
                                string configKey = secretEntry.Name[prefix.Length..].Replace("/", ":");
                                secrets[configKey] = getResponse.SecretString;
                                secretsLoadedFromPrefix++;
                                logger.LogDebug("Loaded secret as config key '{ConfigKey}'", configKey);
                            }
                        }
                        catch (Exception ex) when (!timeLimitReached.IsCancellationRequested)
                        {
                            logger.LogError(ex, "Failed to load a secret from prefix '{Prefix}'", prefix);
                        }
                    }

                    listRequest.NextToken = listResponse.NextToken;
                } while (!string.IsNullOrEmpty(listRequest.NextToken));

                logger.LogInformation("Loaded {Count} secrets from Secrets Manager prefix '{Prefix}'",
                    secretsLoadedFromPrefix, prefix);
            }
            catch (OperationCanceledException) when (timeLimitReached.IsCancellationRequested)
            {
                logger.LogWarning(
                    "AWS Secrets Manager did not answer within {Seconds} seconds, so startup goes on without the secrets it did not read. " +
                    "Check that this host reaches Secrets Manager and that its AWS sign-in is current.",
                    _secretsManagerTimeLimit.TotalSeconds);
                return secrets;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to list secrets with prefix '{Prefix}'", prefix);
            }
        }

        return secrets;
    }
}
