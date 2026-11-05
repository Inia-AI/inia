namespace IniaAi.WebApi.Contracts.Responses.Organizations;

/// <summary>
/// Represents a tenant (organization) that a user belongs to.
/// </summary>
public class UserTenantDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the tenant.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the tenant.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// What this organization may use, as capability names such as <c>run</c> or <c>run-logs</c>. The
    /// server refuses an endpoint whose capability is missing, on top of its role gate; a client hides it.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; set; } = [];
}