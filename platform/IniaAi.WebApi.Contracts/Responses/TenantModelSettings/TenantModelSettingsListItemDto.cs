using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.TenantModelSettings;

/// <summary>
/// Represents a tenant model settings entry in a list view.
/// </summary>
public class TenantModelSettingsListItemDto
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant (organization) ID this setting applies to.
    /// NULL indicates a global default.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the tenant name for display purposes.
    /// Shows "Global Default" when this is a global setting.
    /// </summary>
    public string TenantDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the usage context this setting applies to.
    /// </summary>
    public UsageContext UsageContext { get; set; }

    /// <summary>
    /// Gets or sets the model pricing ID.
    /// </summary>
    public string ModelPricingId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model display name.
    /// </summary>
    public string ModelDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model provider.
    /// </summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this setting is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the priority for ordering.
    /// </summary>
    public int Priority { get; set; }
}
