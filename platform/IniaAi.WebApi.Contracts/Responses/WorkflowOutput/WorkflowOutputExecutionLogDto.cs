namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents detailed content for a single block's execution log.
/// Used for lazy-loading prompt, output, and placeholder details when a log item is expanded.
/// Summary/metadata fields (SectionName, BlockName, Status, etc.) are provided by WorkflowExecutionLogSummaryDto.
/// </summary>
public class WorkflowOutputExecutionLogDto
{
    /// <summary>
    /// Unique identifier for the execution log entry.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the dictionary of inputs used by the block (resolved placeholder values).
    /// </summary>
    public Dictionary<string, string?> Inputs { get; set; } = [];

    /// <summary>
    /// The template prompt sent to the language model (if applicable).
    /// Only populated when includePrompt=true.
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Placeholder values used in the prompt, if any.
    /// Only populated when includePrompt=true.
    /// </summary>
    public Dictionary<string, PlaceholderValueDto>? PromptPlaceholderValues { get; set; }

    /// <summary>
    /// The raw output returned by the block or model.
    /// Only populated when includeOutput=true.
    /// </summary>
    public string? Output { get; set; }
}
