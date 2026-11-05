namespace IniaAi.Configuration.Platform;

/// <summary>
/// Per-tenant model ID overrides for advanced and basic models.
/// Values are catalogue model ids; the connector resolves each to the id the deployment calls.
/// </summary>
public class TenantModelOverride
{
    /// <summary>
    /// Model ID string for advanced model (e.g. "claude-opus-4-6", "claude-sonnet-4-6").
    /// </summary>
    public string? AdvancedModel { get; set; }

    /// <summary>
    /// Model ID string for basic model (e.g. "claude-haiku-4-5-20251001").
    /// </summary>
    public string? BasicModel { get; set; }

    /// <summary>
    /// Model an Agent or Verification block on its advanced tier runs for this tenant, overriding
    /// <c>AgentRuntimeSettings.AdvancedModel</c>. A provider-neutral id (e.g. "claude-opus-5-5")
    /// resolved against <c>AgentRuntimeSettings.AllowedModels</c> when a run starts.
    /// </summary>
    public string? AgentAdvancedModel { get; set; }

    /// <summary>
    /// Model an Agent or Verification block on its basic tier runs for this tenant, overriding
    /// <c>AgentRuntimeSettings.BasicModel</c>. Resolved like <see cref="AgentAdvancedModel"/>.
    /// </summary>
    public string? AgentBasicModel { get; set; }
}
