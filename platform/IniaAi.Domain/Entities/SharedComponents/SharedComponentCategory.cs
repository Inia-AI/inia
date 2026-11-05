using IniaAi.Domain.Common;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SharedComponents;

/// <summary>
/// One entry of the Library's curated category vocabulary — the flat browse spine components file
/// under. Rows are the source of truth (editor-curated, no code constants beyond the initial seed);
/// components reference an entry by plain <see cref="SharedComponent.CategoryId"/> string with no
/// foreign key, so referential integrity is enforced at the application layer: writes validate the
/// id against this table, and a category cannot be deleted while any component references it.
/// </summary>
public class SharedComponentCategory : Entity<SharedComponentCategory>
{
    public const int NAME_MAX_LENGTH = 250;
    public const int DESCRIPTION_MAX_LENGTH = 1000;

    [Required]
    [StringLength(NAME_MAX_LENGTH)]
    public string Name { get; set; } = string.Empty;

    [StringLength(DESCRIPTION_MAX_LENGTH)]
    public string? Description { get; set; }

    public int? DisplayOrder { get; set; }
}
