using IniaAi.Configuration.Enums;

namespace IniaAi.Configuration.Platform;

/// <summary>
/// Configuration for routing AI operations to specific model families.
/// Supports both tenant-level defaults and function-specific overrides.
/// </summary>
public class ModelRoutingSettings
{
    /// <summary>
    /// Default model family to use when no tenant or function-specific routing is defined.
    /// </summary>
    public ModelFamily DefaultModelFamily { get; set; } = ModelFamily.Anthropic;

    /// <summary>
    /// Tenant-level default model families.
    /// Used as fallback when no function-specific override exists.
    /// </summary>
    public Dictionary<string, ModelFamily> TenantDefaults { get; set; } = [];

    /// <summary>
    /// Function-specific model family overrides per tenant.
    /// Allows granular control over which model family handles specific operations.
    /// </summary>
    public Dictionary<string, Dictionary<FunctionType, ModelFamily>> FunctionOverrides { get; set; } = [];

    /// <summary>
    /// Per-tenant model ID overrides. When set, these model IDs are passed to the connector,
    /// overriding the global AdvancedModel/BasicModel from provider settings. Each value must be a
    /// catalogue model id: the connector resolves it to the id this deployment calls and fails the
    /// call when the deployment cannot call that model.
    /// </summary>
    public Dictionary<string, TenantModelOverride> TenantModelOverrides { get; set; } = [];

    /// <summary>
    /// Gets the model family for a specific tenant and function.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (case-insensitive)</param>
    /// <param name="functionType">Type of AI operation being performed</param>
    /// <returns>Model family to use for this operation</returns>
    public ModelFamily GetModelFamily(string? tenantId, FunctionType functionType)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return DefaultModelFamily;
        }

        string normalizedTenantId = tenantId.ToLowerInvariant();

        // Check for function-specific override first
        if (FunctionOverrides.TryGetValue(normalizedTenantId, out Dictionary<FunctionType, ModelFamily>? functionMappings) &&
            functionMappings.TryGetValue(functionType, out ModelFamily functionFamily))
        {
            return functionFamily;
        }

        // Fall back to tenant default
        KeyValuePair<string, ModelFamily> match = TenantDefaults
            .FirstOrDefault(kvp =>
                string.Equals(kvp.Key, normalizedTenantId, StringComparison.OrdinalIgnoreCase));

        if (!match.Equals(default(KeyValuePair<string, ModelFamily>)))
        {
            return match.Value;
        }

        // Final fallback to global default
        return DefaultModelFamily;
    }

    /// <summary>
    /// Gets the model family for a tenant (backward compatibility).
    /// Uses GeneralReasoning as the function type.
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <returns>Model family for general operations</returns>
    public ModelFamily GetModelFamily(string? tenantId)
    {
        return GetModelFamily(tenantId, FunctionType.GeneralReasoning);
    }

    /// <summary>
    /// Gets the model ID override for a specific tenant, if configured.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (case-insensitive)</param>
    /// <param name="shouldUseAdvancedModel">Whether to return the advanced or basic model override</param>
    /// <returns>Model ID string, or null if no override is configured</returns>
    public string? GetModelOverride(string? tenantId, bool shouldUseAdvancedModel)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        string normalizedTenantId = tenantId.ToLowerInvariant();

        KeyValuePair<string, TenantModelOverride> match = TenantModelOverrides
            .FirstOrDefault(kvp =>
                string.Equals(kvp.Key, normalizedTenantId, StringComparison.OrdinalIgnoreCase));

        return match.Key is null ? null : shouldUseAdvancedModel ? match.Value.AdvancedModel : match.Value.BasicModel;
    }

    /// <summary>
    /// Gets the Agent and Verification tier model override for a specific tenant, if configured.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (case-insensitive)</param>
    /// <param name="shouldUseAdvancedModel">Whether to return the advanced or basic agent model override</param>
    /// <returns>Provider-neutral model id, or null if no override is configured</returns>
    public string? GetAgentModelOverride(string? tenantId, bool shouldUseAdvancedModel)
    {
        TenantModelOverride? match = FindTenantModelOverride(tenantId);
        string? model = shouldUseAdvancedModel ? match?.AgentAdvancedModel : match?.AgentBasicModel;

        return string.IsNullOrWhiteSpace(model) ? null : model;
    }

    private TenantModelOverride? FindTenantModelOverride(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || TenantModelOverrides is null)
        {
            return null;
        }

        return TenantModelOverrides
            .FirstOrDefault(kvp => string.Equals(kvp.Key, tenantId, StringComparison.OrdinalIgnoreCase))
            .Value;
    }
}
