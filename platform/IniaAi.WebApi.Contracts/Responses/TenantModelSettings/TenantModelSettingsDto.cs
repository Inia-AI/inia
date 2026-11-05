using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.TenantModelSettings;

/// <summary>
/// Represents full details of a tenant model settings entry.
/// </summary>
public class TenantModelSettingsDto
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
    /// NULL when this is a global default.
    /// </summary>
    public string? TenantName { get; set; }

    /// <summary>
    /// Gets or sets the usage context this setting applies to.
    /// </summary>
    public UsageContext UsageContext { get; set; }

    /// <summary>
    /// Gets or sets the model pricing ID to use for this context.
    /// </summary>
    public string ModelPricingId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model display name for convenience.
    /// </summary>
    public string ModelDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the model provider for display purposes.
    /// </summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this setting is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the priority for ordering when multiple settings exist.
    /// Higher priority takes precedence.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
