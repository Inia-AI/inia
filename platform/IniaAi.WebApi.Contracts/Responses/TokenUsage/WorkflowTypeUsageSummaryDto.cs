namespace IniaAi.WebApi.Contracts.Responses.TokenUsage;

/// <summary>
/// Represents token usage summary for a specific workflow type.
/// </summary>
public class WorkflowTypeUsageSummaryDto
{
    /// <summary>
    /// Gets or sets the workflow type ID.
    /// </summary>
    public string WorkflowTypeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the workflow type name.
    /// </summary>
    public string WorkflowTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total input tokens.
    /// </summary>
    public long TotalInputTokens { get; set; }

    /// <summary>
    /// Gets or sets the total output tokens.
    /// </summary>
    public long TotalOutputTokens { get; set; }

    /// <summary>
    /// Gets or sets the total tokens (input + output).
    /// </summary>
    public long TotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the total embedding tokens (from document processing).
    /// </summary>
    public long TotalEmbeddingTokens { get; set; }

    /// <summary>
    /// Gets or sets the total cost in USD.
    /// </summary>
    public decimal TotalCost { get; set; }

    /// <summary>
    /// Gets or sets the total number of executions.
    /// </summary>
    public int ExecutionCount { get; set; }

    /// <summary>
    /// Gets or sets the average tokens per execution.
    /// </summary>
    public long AverageTokensPerExecution { get; set; }

    /// <summary>
    /// Gets or sets the average cost per execution.
    /// </summary>
    public decimal AverageCostPerExecution { get; set; }

    /// <summary>
    /// Gets or sets the breakdown by usage type.
    /// </summary>
    public List<UsageByTypeDto> ByUsageType { get; set; } = [];

    /// <summary>
    /// Gets or sets the recent execution usage list.
    /// </summary>
    public List<ExecutionUsageSummaryDto> RecentExecutions { get; set; } = [];
}

/// <summary>
/// Represents usage summary for a single workflow execution.
/// </summary>
public class ExecutionUsageSummaryDto
{
    /// <summary>
    /// Gets or sets the workflow execution ID.
    /// </summary>
    public string WorkflowExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user ID who ran the execution.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the user's display name.
    /// </summary>
    public string? UserDisplayName { get; set; }

    /// <summary>
    /// Gets or sets the total tokens.
    /// </summary>
    public long TotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the total cost.
    /// </summary>
    public decimal TotalCost { get; set; }

    /// <summary>
    /// Gets or sets the execution date.
    /// </summary>
    public DateTime ExecutedAt { get; set; }
}
