namespace IniaAi.WebApi.Contracts.Requests.SharedComponents;

/// <summary>
/// Creates or updates one entry of the Library's curated category vocabulary. The id in the route
/// is a stable kebab-case id chosen at creation; renames touch only the display name.
/// </summary>
/// <param name="Name">The category's display name.</param>
/// <param name="Description">Optional description of what files under this category.</param>
/// <param name="DisplayOrder">Explicit ordering rank; entries without one sort after ranked entries, by name.</param>
public record SaveSharedComponentCategoryRequest(
    string Name,
    string? Description = null,
    int? DisplayOrder = null);
