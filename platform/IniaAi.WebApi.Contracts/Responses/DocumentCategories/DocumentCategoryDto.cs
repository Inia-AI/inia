namespace IniaAi.WebApi.Contracts.Responses.DocumentCategories;

/// <summary>
/// Data Transfer Object for a Document Category.
/// </summary>
public class DocumentCategoryDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the document category.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the document category.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display order for sorting categories.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
