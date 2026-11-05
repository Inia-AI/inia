using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Lightweight summary of a workflow execution log for list views.
/// Does not include heavy fields like Prompt, Output, or PromptPlaceholderValues.
/// Use GET /workflow-executions/{executionId}/logs/{logId} to retrieve full details.
/// </summary>
public class WorkflowExecutionLogSummaryDto
{
    /// <summary>
    /// Unique identifier for the execution log entry.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The name of the section (workflow or subsection) that this block belongs to.
    /// </summary>
    public string SectionName { get; set; } = string.Empty;

    /// <summary>
    /// The display name of the executed block.
    /// </summary>
    public string BlockName { get; set; } = string.Empty;

    /// <summary>
    /// The type of the block (e.g., AiQuery, Merge, etc.).
    /// </summary>
    public string? BlockType { get; set; }

    /// <summary>
    /// The replacement tag used to refer to this block's output.
    /// </summary>
    public string? ReplacementTag { get; set; }

    /// <summary>
    /// The model or method used for execution (e.g., "GPT-4", "Claude 3").
    /// </summary>
    public string? ModelUsed { get; set; }

    /// <summary>
    /// The execution status of the block (e.g., Completed, Failed, Skipped).
    /// </summary>
    public BlockExecutionStatus Status { get; set; }

    /// <summary>
    /// If the block failed, this field contains the associated error message.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Set when the block or page failed because the model refused it: the refusal category
    /// (such as <c>reasoning_extraction</c>), <c>unspecified</c>, or <c>written</c> for a refusal
    /// the model wrote as its answer. Null for every other row.
    /// </summary>
    public string? RefusalCategory { get; set; }

    /// <summary>
    /// Timestamp indicating when the block was created or started executing.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Show the log in the UI by default.
    /// </summary>
    public bool ShouldDisplayInLogs { get; set; }

    /// <summary>
    /// Indicates whether this log has prompt content available for detail view.
    /// </summary>
    public bool HasPrompt { get; set; }

    /// <summary>
    /// Indicates whether this log has output content available for detail view.
    /// </summary>
    public bool HasOutput { get; set; }

    /// <summary>
    /// Indicates whether this log has input values.
    /// </summary>
    public bool HasInputs { get; set; }
}
