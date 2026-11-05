using Finbuckle.MultiTenant.Abstractions;

namespace IniaAi.Infrastructure.MultiTenant;

/// <summary>
/// Represents tenant information for multi-tenant support using Organization as the tenant.
/// </summary>
public class OrganizationTenantInfo : ITenantInfo
{
    /// <summary>
    /// Gets or sets the tenant identifier (Organization.Id).
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier for the tenant (uses Organization.Id for simplicity).
    /// </summary>
    public string Identifier { get; set; }

    /// <summary>
    /// Gets or sets the tenant name (Organization.Name).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the connection string for the tenant (optional, for future per-tenant databases).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets whether this tenant is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets when the tenant was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets when the tenant was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    public OrganizationTenantInfo(string id, string identifier)
    {
        Id = id;
        Identifier = identifier;
    }
}
