using System.Text.RegularExpressions;

namespace IniaAi.AiConnection.Common;

/// <summary>
/// Normalizes model IDs from various API sources (Public API, AWS Bedrock, regional variants)
/// to a consistent format for analytics and cost tracking.
/// </summary>
/// <remarks>
/// Examples:
/// <list type="bullet">
/// <item>"claude-sonnet-4-5-20250929" → "claude-sonnet-4-5-20250929" (Public API - unchanged)</item>
/// <item>"anthropic.claude-sonnet-4-5-20250929-v1:0" → "claude-sonnet-4-5-20250929" (AWS Bedrock)</item>
/// <item>"us.anthropic.claude-opus-4-5-20251101-v1:0" → "claude-opus-4-5-20251101" (AWS Bedrock regional)</item>
/// <item>"eu.anthropic.claude-haiku-4-5-20251001-v2:0" → "claude-haiku-4-5-20251001" (AWS Bedrock EU)</item>
/// <item>"claude-sonnet-5" registered as an Azure AI Foundry deployment → "claude-sonnet-5", provider "azure-foundry"</item>
/// </list>
/// A deployment name is whatever the operator called the deployment, so it cannot be parsed
/// from the string: the names a deployment serves are registered once at startup
/// (<see cref="RegisterDeployments"/>) and consulted before any parse. The overloads taking a
/// <see cref="ModelDeploymentTable"/> answer against an explicit table instead of the
/// registered one.
/// </remarks>
public static partial class ModelIdNormalizer
{
    /// <summary>
    /// Metadata key used to store the model ID in response metadata.
    /// </summary>
    public const string MODEL_METADATA_KEY = "Model";

    /// <summary>
    /// Metadata key for the model id the connector sent the request to, such as a Bedrock
    /// inference profile (<c>us.anthropic.claude-sonnet-5-5</c>). Its prefix decides whether
    /// Bedrock bills the call at the regional or the global rate.
    /// </summary>
    public const string ENDPOINT_MODEL_METADATA_KEY = "EndpointModel";

    // Pattern to match AWS Bedrock model ID format:
    // Optional regional prefix (us., eu., global., etc.) +
    // Optional provider prefix (anthropic., etc.) +
    // Model name +
    // Optional version suffix (-v1:0, -v2:0, etc.)
    private static readonly Regex BedrockModelPattern = BedrockModelRegex();

    // A Cohere model on Bedrock, as a foundation model (cohere.embed-english-v3) or through an
    // inference profile (eu.cohere.embed-v4:0). The price list names it without the profile's
    // region and the ":0" revision. On Cohere's own API the name carries its version after a
    // dot (embed-v4.0), so it is already the name the price list uses.
    private static readonly Regex CohereBedrockPattern = CohereBedrockRegex();
    private const string COHERE_API_MODEL_PREFIX = "embed-";

    private static readonly Lock _deploymentsLock = new();
    private static volatile ModelDeploymentTable _deployments = ModelDeploymentTable.Empty;

    /// <summary>
    /// The deployment names registered so far, as an immutable snapshot.
    /// </summary>
    public static ModelDeploymentTable Deployments => _deployments;

    /// <summary>
    /// Registers the names <paramref name="provider"/> identifies its models by. Additive and
    /// thread-safe; refuses a name the provider's shape rule rejects or one another provider
    /// already owns (<see cref="ModelDeploymentTable.With"/>).
    /// </summary>
    public static void RegisterDeployments(string provider, IEnumerable<string> deploymentNames)
    {
        lock (_deploymentsLock)
        {
            _deployments = _deployments.With(provider, deploymentNames);
        }
    }

    /// <summary>
    /// Normalizes a model ID to a consistent format regardless of source (Public API or AWS Bedrock).
    /// </summary>
    /// <param name="modelId">The raw model ID from the API response.</param>
    /// <returns>The normalized model ID, or the original if normalization fails.</returns>
    public static string Normalize(string? modelId)
    {
        return Normalize(modelId, _deployments);
    }

    /// <summary>
    /// <see cref="Normalize(string?)"/> against an explicit deployment table: a registered
    /// deployment name is its own normalized id, whatever it looks like.
    /// </summary>
    public static string Normalize(string? modelId, ModelDeploymentTable deployments)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return ModelProviders.UNKNOWN;
        }

        if (deployments.TryGetProvider(modelId, out _))
        {
            return modelId;
        }

        Match cohereMatch = CohereBedrockPattern.Match(modelId);
        if (cohereMatch.Success)
        {
            return cohereMatch.Groups["model"].Value;
        }

        if (modelId.StartsWith(COHERE_API_MODEL_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            return modelId;
        }

        // If it doesn't contain any prefixes or suffixes, return as-is (Public API format)
        if (!modelId.Contains('.') && !modelId.Contains(":"))
        {
            return modelId;
        }

        // Try to match AWS Bedrock format
        Match match = BedrockModelPattern.Match(modelId);
        if (match.Success)
        {
            return match.Groups["model"].Value;
        }

        // Fallback: try simple extraction
        return ExtractModelName(modelId);
    }

    /// <summary>
    /// Extracts the model name using simple string manipulation as a fallback.
    /// </summary>
    private static string ExtractModelName(string modelId)
    {
        string result = modelId;

        // Remove regional prefix (us., eu., global., ap., etc.)
        if (result.Contains('.'))
        {
            // Find the start of "claude" or "mistral" or other model names
            int claudeIndex = result.IndexOf("claude", StringComparison.OrdinalIgnoreCase);
            int mistralIndex = result.IndexOf("mistral", StringComparison.OrdinalIgnoreCase);

            int modelStart = -1;
            if (claudeIndex >= 0 && mistralIndex >= 0)
            {
                modelStart = Math.Min(claudeIndex, mistralIndex);
            }
            else if (claudeIndex >= 0)
            {
                modelStart = claudeIndex;
            }
            else if (mistralIndex >= 0)
            {
                modelStart = mistralIndex;
            }

            if (modelStart > 0)
            {
                result = result[modelStart..];
            }
        }

        // Remove version suffix (-v1:0, -v2:0, or -v1 without colon)
        int versionIndex = result.LastIndexOf("-v", StringComparison.OrdinalIgnoreCase);

        if (versionIndex <= 0)
        {
            return result;
        }

        // Check that what follows -v is a digit (to avoid matching e.g. "-value")
        string afterV = result[(versionIndex + 2)..];

        if (afterV.Length > 0 && char.IsDigit(afterV[0]))
        {
            result = result[..versionIndex];
        }

        return result;
    }

    /// <summary>
    /// Checks if a model ID appears to be from AWS Bedrock (has prefixes/suffixes).
    /// </summary>
    public static bool IsBedrockFormat(string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) && (modelId.Contains("anthropic.") ||
               modelId.Contains("-v1:") ||
               modelId.Contains("-v2:") ||
               CohereBedrockPattern.IsMatch(modelId));
    }

    /// <summary>
    /// Gets the provider/gateway from a model ID.
    /// Provider represents the API endpoint/gateway used (e.g., AWS Bedrock vs direct API).
    /// </summary>
    /// <param name="modelId">The raw model ID from the API response.</param>
    /// <returns>The provider/gateway name (e.g., "aws-bedrock", "anthropic-api", "azure-foundry").</returns>
    public static string GetProvider(string? modelId)
    {
        return GetProvider(modelId, _deployments);
    }

    /// <summary>
    /// <see cref="GetProvider(string?)"/> against an explicit deployment table: a registered
    /// deployment name answers its provider before any parse runs; everything else parses as
    /// before.
    /// </summary>
    public static string GetProvider(string? modelId, ModelDeploymentTable deployments)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return ModelProviders.UNKNOWN;
        }

        if (deployments.TryGetProvider(modelId, out string? registeredProvider))
        {
            return registeredProvider;
        }

        // Check if it's AWS Bedrock format
        if (IsBedrockFormat(modelId))
        {
            return ModelProviders.AWS_BEDROCK;
        }

        // Direct API providers based on model name prefix
        return modelId.StartsWith("claude", StringComparison.OrdinalIgnoreCase)
            ? ModelProviders.ANTHROPIC_API
            : modelId.StartsWith("cohere", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith(COHERE_API_MODEL_PREFIX, StringComparison.OrdinalIgnoreCase)
            ? ModelProviders.COHERE_API
            : ModelProviders.UNKNOWN;
    }

    /// <summary>
    /// Gets the model family/vendor from a model ID.
    /// Family represents the model vendor (e.g., Anthropic) regardless of access path.
    /// </summary>
    /// <param name="modelId">The raw model ID from the API response.</param>
    /// <returns>The model family/vendor name (e.g., "anthropic").</returns>
    public static string GetFamily(string? modelId)
    {
        return GetFamily(modelId, _deployments);
    }

    /// <summary>
    /// <see cref="GetFamily(string?)"/> against an explicit deployment table.
    /// </summary>
    public static string GetFamily(string? modelId, ModelDeploymentTable deployments)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return ModelProviders.UNKNOWN;
        }

        // Normalize first to get clean model name
        string normalizedId = Normalize(modelId, deployments);

        // Check for known model families
        return normalizedId.StartsWith("claude", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("anthropic.", StringComparison.OrdinalIgnoreCase)
            ? "anthropic"
            : normalizedId.StartsWith("cohere", StringComparison.OrdinalIgnoreCase) ||
            normalizedId.StartsWith(COHERE_API_MODEL_PREFIX, StringComparison.OrdinalIgnoreCase)
            ? "cohere"
            : ModelProviders.UNKNOWN;
    }

    // Regex pattern explanation:
    // ^(?:(?<region>\w+)\.)?        - Optional regional prefix (us., eu., etc.)
    // (?:anthropic\.)?    - Optional provider prefix
    // (?<model>claude-[\w\-]+?)  - The model name (non-greedy)
    // (?:-v\d+(?::\d+)?)?$          - Optional version suffix (-v1:0, -v2:0, or -v1 without colon)
    [GeneratedRegex(
        @"^(?:(?<region>\w+)\.)?(?:anthropic\.)?(?<model>claude-[\w\-]+?)(?:-v\d+(?::\d+)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BedrockModelRegex();

    [GeneratedRegex(
        @"^(?:[a-z]+\.)?(?<model>cohere\.[\w\-]+?)(?::\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CohereBedrockRegex();
}
