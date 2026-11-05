namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// The Library's category vocabulary in display order, plus the count of components that carry no
/// category yet (legacy rows predating the catalogue; new components are always born categorized).
/// </summary>
/// <param name="Categories">The curated vocabulary, ordered for display.</param>
/// <param name="UncategorizedCount">How many registry components carry no category.</param>
public record SharedComponentCategoriesDto(
    List<SharedComponentCategoryDto> Categories,
    int UncategorizedCount);
