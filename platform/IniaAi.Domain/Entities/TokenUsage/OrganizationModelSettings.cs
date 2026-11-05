using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// Stores per-organization model settings for different usage contexts.
/// Allows setting default models for block execution, embeddings, etc.
/// When OrganizationId is null, this represents a global default.
/// </summary>
public class OrganizationModelSettings : Entity<OrganizationModelSettings>
{
    /// <summary>
    /// The organization this setting applies to.
    /// NULL indicates a global default that applies to all organizations.
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// The usage context this setting applies to (e.g., BlockExecution, Embedding).
    /// </summary>
    public UsageContext UsageContext { get; set; }

    /// <summary>
    /// The model pricing record to use for this context.
    /// </summary>
    public string ModelPricingId { get; set; } = string.Empty;

    /// <summary>
    /// Whether this setting is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Priority for ordering when multiple settings exist.
    /// Higher priority takes precedence.
    /// </summary>
    public int Priority { get; set; } = 0;

    // ═══════════════════════════════════════════════════════════════
    // NAVIGATION PROPERTIES
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Navigation property to the organization.
    /// </summary>
    public Organization? Organization { get; set; }
}
