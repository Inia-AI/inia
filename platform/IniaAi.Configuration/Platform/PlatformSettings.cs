using Amazon.Util;
using IniaAi.Configuration.Data;
using IniaAi.Configuration.LLMProviders;
using IniaAi.Configuration.Messaging;
using IniaAi.Configuration.Platform;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Configuration;

public class PlatformSettings : ICloneable
{
    private static readonly CloudHost _cloudHost = new(
        Environment.GetEnvironmentVariable,
        () => EC2InstanceMetadata.InstanceId is not null);

    public AnthropicSettings Anthropic { get; set; } = new AnthropicSettings();

    public CohereSettings Cohere { get; set; } = new CohereSettings();

    public AzureFoundrySettings AzureFoundry { get; set; } = new AzureFoundrySettings();



    public GitHubSettings GitHub { get; set; } = new GitHubSettings();

    public IniaAiSettings IniaAi { get; set; } = new IniaAiSettings();

    public MessageProcessorSettings MessageProcessor { get; set; } = new MessageProcessorSettings();

    public EmailExpansionSettings EmailExpansion { get; set; } = new EmailExpansionSettings();

    public ExaSettings Exa { get; set; } = new ExaSettings();

    public ExchangeRateApiSettings ExchangeRateApi { get; set; } = new ExchangeRateApiSettings();

    public AgentRuntimeSettings AgentRuntime { get; set; } = new AgentRuntimeSettings();

    public WebApiCallSettings WebApiCall { get; set; } = new WebApiCallSettings();

    public OutboundAccessSettings OutboundAccess { get; set; } = new OutboundAccessSettings();

    public StoredKeySettings StoredKeys { get; set; } = new StoredKeySettings();

    public OcrSettings OcrSettings { get; set; } = new OcrSettings();

    public ChartRenderingSettings ChartRendering { get; set; } = new ChartRenderingSettings();

    public PdfReaderSettings PdfReader { get; set; } = new PdfReaderSettings();

    public PromptTemplateSettings PromptTemplates { get; set; } = new PromptTemplateSettings();

    public ModelRoutingSettings ModelRouting { get; set; } = new ModelRoutingSettings();

    public EnvironmentBannerSettings EnvironmentBanner { get; set; } = new EnvironmentBannerSettings();

    /// <summary>
    /// When true, regression testing uses LLM semantic comparison for fact similarity scoring.
    /// When false, uses token-based Jaccard similarity (faster, no LLM cost).
    /// </summary>
    public bool UseLlmForRegressionComparison { get; set; } = false;

    /// <summary>
    /// Maximum number of blocks that may execute concurrently within a single workflow execution.
    /// <c>IBlockConcurrencyCap</c> is registered scoped (one per workflow run), so the host-wide
    /// block concurrency is this value times <c>IniaAi.MaxConcurrentJobs</c>. The cap is read once
    /// when its scope is created; runtime changes require a process restart. Set to 1 to reproduce
    /// the sequential engine (the kill switch / rollback).
    /// </summary>
    public int MaxBlockParallelism { get; set; } = 4;

    public string CustomerOrgId { get; set; } = "Default";

    /// <summary>
    /// Paths of assemblies that add block types, relative to the application folder. Each loads at
    /// startup, and every public <c>IBlockExtension</c> in it registers its types.
    /// </summary>
    public List<string> BlockExtensions { get; set; } = [];

    /// <summary>
    /// The name this deployment states as itself on cross-instance artifacts: the
    /// <c>SourceInstance</c> in exported validation case bundles (half of the re-import
    /// supersession key) and the instance label on workflow-version currency cards. Each
    /// consumer keeps its own fallback when the key is unset — the bundle export uses the
    /// request's Host header (so one deployment reachable through several hostnames exports
    /// under several identities), the currency card uses the machine name.
    /// </summary>
    public string? InstanceName { get; set; }

    /// <summary>
    /// The support address the API documentation names. Unset, the documentation names no contact.
    /// </summary>
    public string? ApiDocsContactEmail { get; set; }

    public string? UserDataDbProvider
    {
        get;
        set => field = DatabaseProviders.IsValid(value)
            ? value
            : throw new ArgumentException($"Invalid database server vendor for " +
                $"UserData DB: {value}");
    }

    [Required]
    public string? CoreDbProvider
    {
        get;
        set => field = DatabaseProviders.IsValid(value)
            ? value
            : throw new ArgumentException($"Invalid database server vendor for" +
                $"Core DB: {value}");
    }

    /// <summary>
    /// Whether a model gateway that a closed module supplies serves this host's model calls.
    /// </summary>
    public bool UsesModelGateway { get; set; }

    /// <summary>
    /// Where this host's model calls go (<see cref="CloudHost.ModeFor"/>). Every switch that picks a
    /// model connector or provider reads this, so no two switches can disagree.
    /// </summary>
    public DeploymentMode DeploymentMode => _cloudHost.ModeFor(UsesModelGateway);

    public static bool IsRunningInAws => _cloudHost.IsAws;

    public static bool IsRunningInAzure => _cloudHost.IsAzure;

    public static bool IsInDevEnv => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")?.Contains("Development") ?? false;

    public static bool IsInLocalDevEnv => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "LocalDevelopment";

    public static bool ShouldReseedDbs => Environment.GetEnvironmentVariable("RESEED_DB") == "true";

    /// <summary>
    /// Gets the AWS environment name for secrets path (dev, staging, prod).
    /// Maps from ASPNETCORE_ENVIRONMENT.
    /// </summary>
    public static string CurrentEnvironment => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")?.ToLowerInvariant() switch
    {
        "localdev" or "localdevelopment" or "development" => "dev",
        "staging" => "staging",
        "production" => "production",
        _ => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")?
                .ToLowerInvariant()
            ?? "dev",
    };

    public object Clone()
    {
        return MemberwiseClone();
    }
}
