namespace IniaAi.WebApi.Contracts.Requests.SharedComponents;

/// <summary>
/// Re-files a shared component under a different curated category.
/// </summary>
/// <param name="CategoryId">The target category id; must name an existing vocabulary entry.</param>
public record SetSharedComponentCategoryRequest(string CategoryId);
