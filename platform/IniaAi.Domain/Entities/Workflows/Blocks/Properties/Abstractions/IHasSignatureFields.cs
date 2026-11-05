namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

/// <summary>
/// Marker contract for block properties that expose staff-authored
/// <see cref="SignatureFieldDefinition"/> placeholders. Clients can override the
/// resolved value per workflow via the <c>SignatureFieldOverride</c> table without
/// touching the workflow JSON. Implemented by <see cref="AiQueryProperties"/> and
/// <see cref="MergeProperties"/>; consumers should branch on this interface rather
/// than block type so any future block can opt in by implementing it.
/// </summary>
public interface IHasSignatureFields
{
    /// <summary>
    /// Placeholder name → definition. Null or empty means the block has no
    /// signature fields declared. The setter is exposed so the staff editor
    /// can mutate the dict (add, rename, delete) via the interface without
    /// branching on the concrete property type.
    /// </summary>
    Dictionary<string, SignatureFieldDefinition>? SignatureFields { get; set; }

    /// <summary>
    /// Returns every templated string the block carries. Callers (signature UI,
    /// dependency-graph extractor) scan these for placeholder references without
    /// branching on concrete property type — each implementer surfaces its own
    /// template fields. May yield <see langword="null"/> entries when a template
    /// is unset; callers should null/empty-check.
    /// </summary>
    IEnumerable<string?> GetTemplateSources();

    /// <summary>
    /// The block's templated strings in editor top-to-bottom order, used to sort
    /// signature fields by where their placeholder first appears. Unlike
    /// <see cref="GetTemplateSources"/> (an order-insensitive referenced-check source),
    /// this is ordered and complete. May yield <see langword="null"/> entries.
    /// </summary>
    IEnumerable<string?> GetSignatureOrderingTemplates();
}
