using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

namespace IniaAi.Core.Services.AgentRuntime;

/// <summary>
/// Which model an Agent or Verification block runs for one organization. A block's own
/// <see cref="IAgentRunProperties.Model"/> wins; a blank one leaves the choice to its tier
/// (<see cref="IAgentRunProperties.ShouldUseAdvancedModel"/>). Each tier model comes from the
/// organization's <see cref="TenantModelOverride"/>, else the deployment's
/// <see cref="AgentRuntimeSettings"/>, else the code default. Every consumer that names, gates,
/// starts, bills or shows a block's model reads it here, so they cannot disagree.
/// </summary>
public sealed class AgentModelTiers
{
    private AgentModelTiers(AgentRuntimeSettings runtime, string advancedModel, string basicModel)
    {
        Runtime = runtime;
        AdvancedModel = advancedModel;
        BasicModel = basicModel;
    }

    public AgentRuntimeSettings Runtime { get; }

    /// <summary>The model the advanced tier names, as configured (any provider's form).</summary>
    public string AdvancedModel { get; }

    /// <summary>The model the basic tier names, as configured (any provider's form).</summary>
    public string BasicModel { get; }

    /// <param name="modelRouting">Null, or an <paramref name="organizationId"/> it has no override for, uses the deployment's tiers.</param>
    public static AgentModelTiers For(
        AgentRuntimeSettings runtime,
        ModelRoutingSettings? modelRouting = null,
        string? organizationId = null)
    {
        return new AgentModelTiers(
            runtime,
            TierModel(runtime, modelRouting, organizationId, shouldUseAdvancedModel: true),
            TierModel(runtime, modelRouting, organizationId, shouldUseAdvancedModel: false));
    }

    public string TierModelFor(bool shouldUseAdvancedModel)
    {
        return shouldUseAdvancedModel ? AdvancedModel : BasicModel;
    }

    /// <summary>True when the block names no model of its own, so its tier decides.</summary>
    public static bool UsesTier(IAgentRunProperties props)
    {
        return string.IsNullOrWhiteSpace(props.Model);
    }

    /// <summary>The model the block asks for: its own when set, else its tier's.</summary>
    public string ConfiguredModelOf(IAgentRunProperties props)
    {
        return UsesTier(props) ? TierModelFor(props.ShouldUseAdvancedModel) : props.Model!;
    }

    /// <summary>
    /// The id the block's run is started and billed under: the allowlist's form of
    /// <see cref="ConfiguredModelOf"/>, or that id as given so the runtime's own 400 refuses it.
    /// </summary>
    public string RunModelOf(IAgentRunProperties props)
    {
        return AgentRuntimeModels.RunIdFor(Runtime, ConfiguredModelOf(props))!;
    }

    private static string TierModel(
        AgentRuntimeSettings runtime,
        ModelRoutingSettings? modelRouting,
        string? organizationId,
        bool shouldUseAdvancedModel)
    {
        if (modelRouting?.GetAgentModelOverride(organizationId, shouldUseAdvancedModel) is { } organizationModel)
        {
            return organizationModel;
        }

        string? deploymentModel = shouldUseAdvancedModel ? runtime.AdvancedModel : runtime.BasicModel;
        if (!string.IsNullOrWhiteSpace(deploymentModel))
        {
            return deploymentModel;
        }

        return shouldUseAdvancedModel
            ? AgentRuntimeSettings.DEFAULT_ADVANCED_MODEL
            : AgentRuntimeSettings.DEFAULT_BASIC_MODEL;
    }
}
