namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents a placeholder value in a workflow output, including its state of translation and truncation.
/// </summary>
public class PlaceholderValueDto
{
    /// <summary>
    /// Gets or sets the placeholder value.
    /// </summary>
    public string Value { get; set; } = default!;

    /// <summary>
    /// Indicates whether the placeholder value has been translated.
    /// </summary>
    public bool Translated { get; set; } = false;

    /// <summary>
    /// Indicates whether the placeholder value has been truncated.
    /// </summary>
    public bool Truncated { get; set; } = false;
}
