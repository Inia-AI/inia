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

    /// <summary>
    /// The workflow and the output that make a file of this input class. Null when no workflow makes it or its
    /// workflow no longer makes that output, and always null outside the report type summary read by type.
    /// </summary>
    public DocumentClassMadeByDto? MadeBy { get; init; }
}

/// <summary>The workflow type and the output of it that make a file of an input class.</summary>
/// <param name="WorkflowTypeId">The identifier of the workflow type that makes the file.</param>
/// <param name="WorkflowTypeName">The name of that workflow type.</param>
/// <param name="OutputName">The display name of the output that the file is.</param>
public sealed record DocumentClassMadeByDto(string WorkflowTypeId, string WorkflowTypeName, string OutputName);
