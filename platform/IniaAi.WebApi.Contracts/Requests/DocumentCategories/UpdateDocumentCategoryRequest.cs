using System.ComponentModel.DataAnnotations;

namespace IniaAi.WebApi.Contracts.Requests.DocumentCategories;

/// <summary>
/// Request to update an existing document category.
/// </summary>
public record UpdateDocumentCategoryRequest
{
    /// <summary>
    /// Gets or sets the new name for the document category.
    /// </summary>
    [StringLength(200)]
    public string? Name { get; init; }

    /// <summary>
    /// Gets or sets the new display order for sorting categories.
    /// </summary>
    public int? DisplayOrder { get; init; }
}
