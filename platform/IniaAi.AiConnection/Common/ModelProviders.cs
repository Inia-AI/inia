using IniaAi.Configuration.Platform;

namespace IniaAi.AiConnection.Common;

/// <summary>
/// The provider vocabulary <see cref="ModelIdNormalizer.GetProvider(string?)"/> answers in and
/// <c>ModelPricing.Provider</c> is keyed by, with one row per provider saying how its model ids
/// are shaped. A provider that identifies a model by a name chosen at deployment time (Azure AI
/// Foundry) cannot be parsed from the id, so its names are registered
/// through <see cref="ModelIdNormalizer.RegisterDeployments"/> instead. A new provider is a new
/// row here, never a new parser branch.
/// </summary>
public static class ModelProviders
{
    public const string AWS_BEDROCK = "aws-bedrock";
    public const string ANTHROPIC_API = "anthropic-api";
    public const string AZURE_FOUNDRY = "azure-foundry";
    public const string COHERE_API = "cohere-api";
    public const string UNKNOWN = "unknown";

    private sealed record ProviderRule(bool NamesDeployments, Func<string, string?> DescribeRefusal);

    private static readonly Dictionary<string, ProviderRule> _rules = new(StringComparer.Ordinal)
    {
        [AWS_BEDROCK] = new(
            NamesDeployments: false,
            static modelId => ModelIdNormalizer.IsBedrockFormat(modelId)
                ? null
                : $"'{modelId}' is not an AWS Bedrock inference-profile id (expected a form like "
                  + "'us.anthropic.claude-sonnet-5')"),
        [AZURE_FOUNDRY] = new(
            NamesDeployments: true,
            static modelId => ModelIdNormalizer.IsBedrockFormat(modelId)
                ? $"'{modelId}' is an AWS Bedrock inference-profile id, not an Azure AI Foundry deployment "
                  + "name (expected the name the deployment was given, like 'claude-sonnet-5')"
                : null),
    };

    /// <summary>
    /// The provider a deployment's model calls go through, or null for a direct-API deployment,
    /// where the runtime's allowlist is the only rule. A model gateway takes Bedrock
    /// inference-profile ids and forwards them to Bedrock, so a gateway deployment is
    /// <see cref="AWS_BEDROCK"/> for the id shape, and its runs price on the Bedrock rows.
    /// </summary>
    public static string? ForDeploymentMode(DeploymentMode mode)
    {
        return mode switch
        {
            DeploymentMode.Gateway or DeploymentMode.Aws => AWS_BEDROCK,
            DeploymentMode.Azure => AZURE_FOUNDRY,
            DeploymentMode.DirectApi => null,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown deployment mode."),
        };
    }

    /// <summary>
    /// True when the provider identifies a model by a configured deployment name rather than an
    /// id the normalizer can parse, so its names must be registered to be recognised.
    /// </summary>
    public static bool NamesDeployments(string provider)
    {
        return _rules.TryGetValue(provider, out ProviderRule? rule) && rule.NamesDeployments;
    }

    /// <summary>
    /// Why <paramref name="modelId"/> cannot name a model on <paramref name="provider"/>, or null
    /// when it can. A provider without a shape rule accepts anything.
    /// </summary>
    public static string? DescribeModelIdRefusal(string provider, string modelId)
    {
        return _rules.TryGetValue(provider, out ProviderRule? rule) ? rule.DescribeRefusal(modelId) : null;
    }

    /// <summary>
    /// The first entry of an allowlist that cannot name a model on <paramref name="provider"/>,
    /// described, or null when every entry can.
    /// </summary>
    public static string? DescribeAllowlistRefusal(string provider, IEnumerable<string> modelIds)
    {
        return modelIds
            .Select(modelId => DescribeModelIdRefusal(provider, modelId))
            .FirstOrDefault(static refusal => refusal is not null);
    }
}
