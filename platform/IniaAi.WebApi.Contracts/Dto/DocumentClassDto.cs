namespace IniaAi.WebApi.Contracts.Dto;

/// <summary>
/// Data transfer object representing a document class.
/// </summary>
public record DocumentClassDto
{
    /// <summary>
    /// The identifier of the document class.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// The display name of the document class.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// The description of the document class.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The requirement level of the document class.
    /// </summary>
    public string Requirement { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the document class should be singular.
    /// </summary>
    public bool ShouldBeSingular { get; init; } = false;

    /// <summary>
    /// Indicates whether the document class is an output document.
    /// </summary>
    public bool IsOutputDocument { get; init; }

    /// <summary>
    /// Optional document category ID, stamped on the documents of this class.
    /// </summary>
    public string? DocumentCategoryId { get; init; }

    /// <summary>
    /// What qualifies a document as this class, written for a model rather than a person. Null when not set,
    /// and always null in the report type summary a run's reader gets.
    /// </summary>
    public string? LlmDescription { get; init; }
}
