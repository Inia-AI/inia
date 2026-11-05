using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SharedComponents;

/// <summary>
/// Registry entry for a Library shared component. Carries identity and lifecycle metadata only —
/// the definition content lives exclusively in the head <see cref="SharedComponentVersion"/>
/// payload, never on this row. One global pool per deployment instance (no organization scoping by
/// design; write surfaces are role-gated). Components are retired via <see cref="State"/> and never
/// soft-deleted: a soft-deleted row vanishes from head lookups while consumer copies still point at
/// it. <see cref="CategoryId"/> is the single curated catalogue slot (null = uncategorized legacy
/// row): a plain string against the <see cref="SharedComponentCategory"/> vocabulary, validated at
/// the write mouths rather than by a foreign key.
/// </summary>
public class SharedComponent : Entity<SharedComponent>
{
    public const int NAME_MAX_LENGTH = 250;

    public SharedComponentKind Kind { get; set; }

    [Required]
    [StringLength(NAME_MAX_LENGTH)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [StringLength(450)]
    public string? CategoryId { get; set; }

    public SharedComponentState State { get; set; } = SharedComponentState.Active;
}
