namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// One entry of the Library's curated category vocabulary, with its component count (retired
/// components included).
/// </summary>
/// <param name="Id">The category identifier (a stable kebab-case id).</param>
/// <param name="Name">The category's display name.</param>
/// <param name="Description">The category's description, when set.</param>
/// <param name="DisplayOrder">Explicit ordering rank; entries without one sort after ranked entries, by name.</param>
/// <param name="ComponentCount">How many registry components (including retired ones) carry this category.</param>
public record SharedComponentCategoryDto(
    string Id,
    string Name,
    string? Description,
    int? DisplayOrder,
    int ComponentCount);
