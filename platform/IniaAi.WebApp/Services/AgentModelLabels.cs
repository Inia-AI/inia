using IniaAi.AiConnection.Common;
using IniaAi.Common;
using IniaAi.Common.Extensions;
using AnthropicModel = IniaAi.Configuration.Enums.Anthropic.AiModel;

namespace IniaAi.WebApp.Services;

/// <summary>
/// How the agent-family block editors label a model id from the runtime's allowlist. A
/// Bedrock inference-profile id has a catalogue description; a Foundry deployment is known by
/// the name the operator gave it, so it is shown as that name — the catalogue's description for
/// the same string names no deployment, and a custom name has no description at all.
/// </summary>
public static class AgentModelLabels
{
    private static readonly Dictionary<string, string> _providerLabels = new(StringComparer.Ordinal)
    {
        [ModelProviders.AZURE_FOUNDRY] = "Azure AI Foundry",
    };

    public static string Describe(string? modelId)
    {
        return Describe(modelId, ModelIdNormalizer.Deployments);
    }

    public static string Describe(string? modelId, ModelDeploymentTable deployments)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return string.Empty;
        }

        if (deployments.TryGetProvider(modelId, out string? provider))
        {
            return _providerLabels.TryGetValue(provider, out string? providerLabel)
                ? $"{modelId} ({providerLabel})"
                : modelId;
        }

        return Utils.TryParseEnumFromEnumMemberAttribute(modelId, out AnthropicModel model)
            ? model.GetDescription()
            : modelId;
    }

    /// <summary>
    /// Catalogue models sort by their tier order; a registered deployment name keeps the order
    /// the allowlist was configured in.
    /// </summary>
    public static int VisualOrder(string modelId)
    {
        return VisualOrder(modelId, ModelIdNormalizer.Deployments);
    }

    public static int VisualOrder(string modelId, ModelDeploymentTable deployments)
    {
        if (deployments.TryGetProvider(modelId, out _))
        {
            return int.MaxValue;
        }

        int order = Utils.TryParseEnumFromEnumMemberAttribute(modelId, out AnthropicModel model)
            ? model.GetVisualOrder()
            : -1;
        return order > 0 ? order : int.MaxValue;
    }
}
