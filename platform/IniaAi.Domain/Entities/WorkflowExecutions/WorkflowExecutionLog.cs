using IniaAi.Common;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Represents a detailed execution log for a single block within a workflow section.
/// Captures inputs, outputs, status, metadata, and diagnostics.
/// </summary>
public class WorkflowExecutionLog
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the name of the section (i.e., the parent workflow or child section).
    /// </summary>
    public string SectionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the block.
    /// </summary>
    public string BlockName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of the block (e.g., AiQuery, Merge, etc.).
    /// </summary>
    public string? BlockType { get; set; }

    /// <summary>
    /// Gets or sets the replacement tag used to refer to this block's output.
    /// </summary>
    public string? ReplacementTag { get; set; }

    /// <summary>
    /// Gets or sets the dictionary of inputs used by the block (resolved placeholder values).
    /// </summary>
    public Dictionary<string, string?> Inputs { get; set; } = [];

    /// <summary>
    /// Gets or sets the final prompt that was sent to the model or used in this block (if applicable).
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// Gets or sets the final output or response produced by the block.
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// Gets or sets the confidence score assigned to the block output, if applicable.
    /// </summary>
    public double? ConfidenceScore { get; set; }

    /// <summary>
    /// Gets or sets the list of sources used or referenced by the block's output.
    /// </summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>
    /// Gets or sets the status of the block execution (Completed, Failed, Skipped).
    /// </summary>
    public BlockExecutionStatus Status { get; set; } = BlockExecutionStatus.Completed;

    /// <summary>
    /// Gets or sets the error message if the block execution failed.
    /// </summary>
    public string? Error
    {
        get;
        set => field = value.RemoveNullCharacters();
    }

    /// <summary>
    /// Set when the block or page failed because the model refused it: the provider's refusal
    /// category (such as <c>reasoning_extraction</c>), <c>unspecified</c> when it named none, or
    /// <c>written</c> for a refusal the model wrote as its answer. Null for every other row.
    /// </summary>
    [StringLength(64)]
    public string? RefusalCategory { get; set; }

    /// <summary>
    /// Gets or sets the duration it took to execute the block.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Gets or sets the model used to execute the block, if applicable (e.g., GPT-4, Claude 3).
    /// </summary>
    public string? ModelUsed { get; set; }

    /// <summary>
    /// Gets or sets the raw or template prompt (prior to input substitution), useful for debugging.
    /// </summary>
    public string? TemplatePrompt { get; set; }

    public bool ShouldDisplayInLogs { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string WorkflowExecutionId { get; set; } = default!;

    public string? WorkflowId { get; set; }

    public string? BlockId { get; set; }

    #region Navigational Properties

    public WorkflowExecution WorkflowExecution { get; set; } = default!;

    public Workflow? Workflow { get; set; } = default!;

    public Block? Block { get; set; } = default!;

    #endregion Navigation Properties
}
