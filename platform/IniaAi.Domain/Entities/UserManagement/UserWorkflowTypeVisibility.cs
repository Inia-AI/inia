using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// The workflow types a user has chosen to see. This is a display filter that reduces
/// clutter, never an access boundary: a user with no rows sees every workflow type their
/// organization can reach, and rows can only ever narrow that set — never widen it.
/// Organization access remains the sole boundary, enforced separately through
/// <c>WorkflowTypeOrganizationAccess</c>.
///
/// <para>
/// Lives in UserData with a string reference to Core.WorkflowType.Id, because the two
/// contexts exclude each other's entities and a foreign key cannot span them.
/// </para>
/// </summary>
[PrimaryKey(nameof(UserId), nameof(WorkflowTypeId))]
public class UserWorkflowTypeVisibility
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the workflow type identifier.
    /// String reference to Core.WorkflowType.Id (cross-context reference).
    /// </summary>
    [Required]
    [StringLength(100)]
    public string WorkflowTypeId { get; set; } = string.Empty;

    #region Navigation Properties

    /// <summary>
    /// Gets or sets the associated user.
    /// </summary>
    public virtual User User { get; set; } = null!;

    #endregion Navigation Properties
}
