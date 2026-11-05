using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// Represents the many-to-many relationship between users and organizations for multi-tenancy support.
/// </summary>
public class UserOrganization
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the organization identifier.
    /// </summary>
    [Required]
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is the user's default organization.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets whether the user's membership in this organization is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the date and time when the user joined the organization.
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the date and time when the user left the organization (null if still active).
    /// </summary>
    public DateTime? LeftAt { get; set; }

    /// <summary>
    /// The staff user who made the assignment in the admin app; empty for one made before it was
    /// recorded, or by sign-up, a seed or a CSV import.
    /// </summary>
    [StringLength(450)]
    public string? AssignedByUserId { get; set; }

    #region Navigation Properties

    /// <summary>
    /// Gets or sets the associated user.
    /// </summary>
    public virtual User User { get; set; } = null!;

    /// <summary>
    /// Gets or sets the associated organization.
    /// </summary>
    public virtual Organization Organization { get; set; } = null!;

    #endregion Navigation Properties
}