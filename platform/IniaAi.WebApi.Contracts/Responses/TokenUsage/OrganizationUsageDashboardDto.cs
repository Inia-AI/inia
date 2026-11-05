namespace IniaAi.WebApi.Contracts.Responses.TokenUsage;

/// <summary>
/// Represents the organization-wide token usage dashboard.
/// </summary>
public class OrganizationUsageDashboardDto
{
    /// <summary>
    /// Gets or sets the organization ID.
    /// </summary>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the organization name.
    /// </summary>
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets today's usage summary.
    /// </summary>
    public TokenUsageSummaryDto Today { get; set; } = new();

    /// <summary>
    /// Gets or sets this week's usage summary.
    /// </summary>
    public TokenUsageSummaryDto ThisWeek { get; set; } = new();

    /// <summary>
    /// Gets or sets this month's usage summary.
    /// </summary>
    public TokenUsageSummaryDto ThisMonth { get; set; } = new();

    /// <summary>
    /// Gets or sets all-time usage summary.
    /// </summary>
    public TokenUsageSummaryDto AllTime { get; set; } = new();

    /// <summary>
    /// Gets or sets the breakdown by usage type.
    /// </summary>
    public List<UsageByTypeDto> ByUsageType { get; set; } = [];

    /// <summary>
    /// Gets or sets the breakdown by workflow type.
    /// </summary>
    public List<UsageByWorkflowTypeDto> ByWorkflowType { get; set; } = [];

    /// <summary>
    /// Gets or sets the breakdown by user.
    /// </summary>
    public List<UsageByUserDto> ByUser { get; set; } = [];

    /// <summary>
    /// Gets or sets the daily usage trend for the last 30 days.
    /// </summary>
    public List<DailyUsageTrendDto> DailyTrend { get; set; } = [];
}

/// <summary>
/// Represents usage breakdown by workflow type.
/// </summary>
public class UsageByWorkflowTypeDto
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
    /// Gets or sets the total tokens.
    /// </summary>
    public long TotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the total cost.
    /// </summary>
    public decimal TotalCost { get; set; }

    /// <summary>
    /// Gets or sets the percentage of total usage.
    /// </summary>
    public decimal Percentage { get; set; }

    /// <summary>
    /// Gets or sets the execution count.
    /// </summary>
    public int ExecutionCount { get; set; }
}

/// <summary>
/// Represents usage breakdown by user.
/// </summary>
public class UsageByUserDto
{
    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

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
    /// Gets or sets the percentage of total usage.
    /// </summary>
    public decimal Percentage { get; set; }

    /// <summary>
    /// Gets or sets the operation count.
    /// </summary>
    public int OperationCount { get; set; }
}

/// <summary>
/// Represents daily usage trend data point.
/// </summary>
public class DailyUsageTrendDto
{
    /// <summary>
    /// Gets or sets the date.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Gets or sets the total tokens for this day.
    /// </summary>
    public long TotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the total cost for this day.
    /// </summary>
    public decimal TotalCost { get; set; }

    /// <summary>
    /// Gets or sets the LLM tokens for this day.
    /// </summary>
    public long LlmTokens { get; set; }

    /// <summary>
    /// Gets or sets the embedding tokens for this day.
    /// </summary>
    public long EmbeddingTokens { get; set; }

    /// <summary>
    /// Gets or sets the operation count for this day.
    /// </summary>
    public int OperationCount { get; set; }
}
