using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.TenantModelSettings;

/// <summary>
/// Represents the request payload to create a new tenant model settings entry.
/// </summary>
public class CreateTenantModelSettingsRequest
{
    /// <summary>
    /// Gets or sets the tenant (organization) ID this setting applies to.
    /// NULL indicates a global default that applies to all tenants.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the usage context this setting applies to.
    /// </summary>
    public UsageContext UsageContext { get; set; }

    /// <summary>
    /// Gets or sets the model pricing ID to use for this context.
    /// </summary>
    public string ModelPricingId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this setting is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the priority for ordering when multiple settings exist.
    /// Higher priority takes precedence.
    /// </summary>
    public int Priority { get; set; } = 0;
}
