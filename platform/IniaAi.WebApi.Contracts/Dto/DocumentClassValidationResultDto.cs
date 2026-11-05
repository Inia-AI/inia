namespace IniaAi.WebApi.Contracts.Dto;

/// <summary>
/// Represents the result of validating document classes for a workflow.
/// </summary>
public record DocumentClassValidationResultDto
{
    /// <summary>
    /// Gets a value indicating whether the validation passed without errors.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets a value indicating whether the validation produced warnings.
    /// </summary>
    public bool HasWarnings { get; init; }

    /// <summary>
    /// Gets the list of validation error messages.
    /// </summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets the list of validation warning messages.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets the status of each document class in the validation.
    /// </summary>
    public List<DocumentClassStatusDto> DocumentClassStatuses { get; init; } = [];
}

/// <summary>
/// Represents the status of a single document class within a validation result.
/// </summary>
public record DocumentClassStatusDto
{
    /// <summary>
    /// Gets the unique key identifying the document class.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the display name of the document class.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the requirement description for this document class.
    /// </summary>
    public required string Requirement { get; init; }

    /// <summary>
    /// Gets a value indicating whether only a single document is expected.
    /// </summary>
    public bool ShouldBeSingular { get; init; }

    /// <summary>
    /// Gets the number of documents uploaded for this class.
    /// </summary>
    public int UploadedCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether the requirement for this document class is satisfied.
    /// </summary>
    public bool IsSatisfied { get; init; }

    /// <summary>
    /// Gets the validation message for this document class, if any.
    /// </summary>
    public string? ValidationMessage { get; init; }
}
