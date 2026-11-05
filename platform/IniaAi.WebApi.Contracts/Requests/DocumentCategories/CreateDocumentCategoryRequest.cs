using System.ComponentModel.DataAnnotations;

namespace IniaAi.WebApi.Contracts.Requests.DocumentCategories;

/// <summary>
/// Request to create a new document category.
/// </summary>
public record CreateDocumentCategoryRequest
{
    /// <summary>
    /// Gets or sets the name of the document category.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets the display order for sorting categories.
    /// </summary>
    public int? DisplayOrder { get; init; }
}
