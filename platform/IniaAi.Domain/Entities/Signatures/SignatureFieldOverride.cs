using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Signatures;

/// <summary>
/// The active client-authored override value for a workflow-level signature field.
/// Exactly one row exists per <c>(OrganizationId, WorkflowTypeId, PlaceholderName)</c> —
/// row absence means the field resolves to the staff-authored default. Override removal
/// is modelled as row deletion, not a sentinel value. <see cref="PlaceholderName"/> is
/// the durable identity: definitions live on the Workflow
/// (<c>WorkflowSignatureField</c>) and one override value applies to every block whose
/// templates reference the <c>{name}</c> token, so block restructuring never orphans a
/// row. Staff renames rewrite this column in the same save transaction.
///
/// <para>
/// Deliberately stored in a dedicated table rather than inline on the workflow so that
/// downloaded <c>.workflow</c> JSON files carry workflow structure only, never client
/// override values. This keeps cross-environment workflow transfer safe from accidental
/// client-data leakage.
/// </para>
/// </summary>
[Index(nameof(OrganizationId), nameof(WorkflowTypeId), nameof(PlaceholderName), IsUnique = true)]
public class SignatureFieldOverride : Entity<SignatureFieldOverride>
{
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string WorkflowTypeId { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string PlaceholderName { get; set; } = string.Empty;

    [Required]
    [StringLength(4000)]
    public string Value { get; set; } = string.Empty;
}
