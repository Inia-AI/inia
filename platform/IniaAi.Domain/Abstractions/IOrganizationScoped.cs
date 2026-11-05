namespace IniaAi.Domain.Abstractions;

/// <summary>
/// Interface for entities that belong to a specific organization.
/// </summary>
public interface IOrganizationScoped
{
    /// <summary>
    /// The organization ID that owns this entity.
    /// </summary>
    string OrganizationId { get; set; }
}