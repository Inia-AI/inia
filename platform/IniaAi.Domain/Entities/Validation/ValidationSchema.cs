using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// The authored catalog of a validation schema: scalar fields and repeating groups with
/// prose extraction descriptions, types, tolerances, and flags, stored as one JSON payload.
/// Cases select a subset of this catalog and snapshot the resolved schema onto themselves,
/// so editing or even deleting a schema never breaks existing cases or runs.
/// <see cref="OrganizationId"/> is nullable on purpose: null plus <see cref="IsPublic"/> is
/// the home for shipped starter schemas, and the consumption filter is public-or-own-org.
/// <see cref="WorkflowTypeId"/> is the consumption key the one-click star and the gap fill
/// resolve cases through: binding it (or changing it) on a saved schema creates cases — with
/// paid reference extractions — for the saving organization's starred reports of that type
/// that have none yet.
/// A schema created by a case-bundle import carries <see cref="SourceInstance"/> and
/// <see cref="SourceSchemaId"/> — where its catalog came from — rather than the exporting
/// deployment's primary key, so a later sibling bundle binds to that entry without an importer
/// ever choosing a local id. The pair is indexed but not unique: a unique index would have to
/// exclude the null pair every locally authored schema carries, and SQL Server treats those nulls
/// as equal. Two bundles imported at the same instant can therefore still create two entries.
/// </summary>
[Index(nameof(OrganizationId), nameof(SourceInstance), nameof(SourceSchemaId))]
public class ValidationSchema : Entity<ValidationSchema>
{
    public const int MAX_NAME_LENGTH = 250;

    /// <summary>
    /// Bound to the case's width rather than restated: the import validator caps the bundle's
    /// source instance against that constant, and the two columns take the same value from the
    /// same bundle. Widening one alone would let a bundle validate and then fail the Core commit.
    /// </summary>
    public const int MAX_SOURCE_INSTANCE_LENGTH = ValidationCase.MAX_SOURCE_INSTANCE_LENGTH;

    [Required]
    [StringLength(MAX_NAME_LENGTH)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [StringLength(450)]
    public string? OrganizationId { get; set; }

    public bool IsPublic { get; set; }

    [StringLength(450)]
    public string? WorkflowTypeId { get; set; }

    public string SchemaJson { get; set; } = string.Empty;

    /// <summary>
    /// The deployment whose case bundle created this schema, null for a locally authored one.
    /// With <see cref="SourceSchemaId"/> this is the cross-instance identity a later sibling
    /// bundle binds by.
    /// </summary>
    [StringLength(MAX_SOURCE_INSTANCE_LENGTH)]
    public string? SourceInstance { get; set; }

    /// <summary>
    /// The schema id the bundle named on the exporting deployment. Never adopted as this row's
    /// primary key: an id an importer accepts from a file is an id an importer can be made to claim.
    /// </summary>
    [StringLength(450)]
    public string? SourceSchemaId { get; set; }

    /// <summary>
    /// True when <paramref name="organizationId"/> is the organization that created the schema —
    /// the only one that may change its visibility or delete it. Shipped schemas with no
    /// organization belong to nobody and keep their visibility.
    /// </summary>
    public bool IsOwnedBy(string organizationId)
    {
        return string.Equals(OrganizationId, organizationId, StringComparison.Ordinal);
    }
}
