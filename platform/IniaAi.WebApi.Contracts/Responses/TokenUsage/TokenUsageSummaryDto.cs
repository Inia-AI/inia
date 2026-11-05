namespace IniaAi.WebApi.Contracts.Responses.TokenUsage;

/// <summary>
/// Represents aggregated token usage summary for a time period.
/// </summary>
public class TokenUsageSummaryDto
{
    /// <summary>
    /// Gets or sets the period label (e.g., "Today", "This Week", "This Month").
    /// </summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the start date of the period.
    /// </summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>
    /// Gets or sets the end date of the period.
    /// </summary>
    public DateTime PeriodEnd { get; set; }

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
    /// Gets or sets the total cache creation tokens.
    /// </summary>
    public long TotalCacheCreationTokens { get; set; }

    /// <summary>
    /// Gets or sets the total cache read tokens.
    /// </summary>
    public long TotalCacheReadTokens { get; set; }

    /// <summary>
    /// Gets or sets the total cost in USD.
    /// </summary>
    public decimal TotalCost { get; set; }

    /// <summary>
    /// Gets or sets the number of operations.
    /// </summary>
    public int OperationCount { get; set; }
}

/// <summary>
/// Represents personal usage summary with multiple time periods.
/// </summary>
public class MyUsageSummaryDto
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
}

/// <summary>
/// Represents usage breakdown by a specific dimension.
/// </summary>
public class UsageByTypeDto
{
    /// <summary>
    /// Gets or sets the usage type.
    /// </summary>
    public string UsageType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name for the usage type.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

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
