using IniaAi.Domain.Common;
using IniaAi.Domain.StoredKeys;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.StoredKeys;

/// <summary>
/// A credential an organization stores for its workflows, such as a third-party API key. A web
/// API call block names it in a header, and the platform fills the value in when it sends the
/// request. The block, its run record and a downloaded <c>.workflow</c> file hold only the name.
/// <see cref="ProtectedValue"/> is the value encrypted by <see cref="IStoredKeyCipher"/>; nothing
/// reads it back except the request that uses it.
/// </summary>
[Index(nameof(OrganizationId), nameof(Name), IsUnique = true)]
public class OrganizationStoredKey : Entity<OrganizationStoredKey>
{
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(StoredKeyNames.MAX_LENGTH)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string ProtectedValue { get; set; } = string.Empty;
}
