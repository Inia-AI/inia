using IniaAi.Domain.Entities.Counterparties;
using Microsoft.AspNetCore.Identity;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// Represents a user in the system.
/// </summary>
public class User : IdentityUser//, IAuditableEntity
{
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the first name of the user.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Gets or sets the last name of the user.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Gets or sets the middle name of the user (optional).
    /// </summary>
    public string? MiddleName { get; set; }

    /// <summary>
    /// Indicates whether this is a special system user used for automated flows.
    /// </summary>
    public bool IsSystem { get; set; }

    //public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    //public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
    //public string? UpdatedByUserId { get; set; }
    //public bool IsDeleted { get; set; } = false;
    //public DateTime? DeletedAt { get; set; }
    //public string? DeletedByUserId { get; set; }

    // Identity counts failed sign-ins toward a lockout only on an account with lockout on, and the
    // seeder, the CSV import and the admin app build accounts directly, not through UserManager.
    public User()
    {
        LockoutEnabled = true;
    }

    public User(Organization organization) : this()
    {
        OrganizationId = organization.Id;
    }

    public User(string organizationId) : this()
    {
        OrganizationId = organizationId;
    }

    private static readonly PasswordHasher<User> _passwordHasher = new();

    /// <summary>
    /// Gets the full name of the user by combining first, middle (if present), and last names.
    /// </summary>
    /// <returns>The formatted full name.</returns>
    public string GetFullName()
    {
        string firstName = FirstName ?? string.Empty;
        string lastName = LastName ?? string.Empty;

        return string.IsNullOrWhiteSpace(MiddleName) ? $"{firstName} {lastName}".Trim() : $"{firstName} {MiddleName} {lastName}".Trim();
    }

    /// <summary>
    /// Sets the user's password by hashing the supplied plain-text value.
    /// </summary>
    public void SetPassword(string rawPassword)
    {
        PasswordHash = _passwordHasher.HashPassword(this, rawPassword);
    }

    /// <summary>
    /// Whether the user is administratively disabled (permanently locked out via <see cref="Disable"/>).
    /// A transient failed-login lockout is deliberately NOT treated as disabled — hence the max-value
    /// sentinel check rather than the "currently locked out" test that login gating uses.
    /// </summary>
    public bool IsDisabled => LockoutEnabled && LockoutEnd == DateTimeOffset.MaxValue;

    /// <summary>
    /// Administratively disables the user by applying a permanent lockout.
    /// </summary>
    public void Disable()
    {
        LockoutEnabled = true;
        LockoutEnd = DateTimeOffset.MaxValue;
    }

    /// <summary>
    /// Lifts an administrative lockout so the user can log in again.
    /// </summary>
    public void Enable()
    {
        LockoutEnd = null;
    }

    #region Navigational Properties
    public Organization? Organization { get; set; }

    public ICollection<UserOrganization> UserOrganizations { get; set; } = [];

    public ICollection<Counterparty> Counterparties { get; private set; } = [];

    #endregion Navigational Properties
}
