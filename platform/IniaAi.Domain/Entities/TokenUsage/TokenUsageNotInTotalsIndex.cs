namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// The filtered index that finds the ledger rows the usage-totals sweep has not added yet, which
/// every usage read sums beside the totals. It covers every column that read touches, IsDeleted
/// included, because SQL Server scans the whole ledger rather than look rows up from a filtered
/// index. SQL Server also does not count rows leaving a filtered index as changes to its statistics
/// (51,374 rows swept, modification counter still 0), so after a backlog the sweep refreshes them,
/// or every read would be planned for a backlog that is gone.
/// </summary>
public static class TokenUsageNotInTotalsIndex
{
    public const string NAME = "IX_TokenUsages_NotInTotals";

    public const string SQL_SERVER_REFRESH_STATISTICS = $"UPDATE STATISTICS [UserData].[TokenUsages] [{NAME}]";

    public static IReadOnlyList<string> IncludedColumns { get; } =
    [
        nameof(TokenUsage.IsDeleted),
        nameof(TokenUsage.UsageType),
        nameof(TokenUsage.WorkflowTypeId),
        nameof(TokenUsage.UserId),
        nameof(TokenUsage.WorkflowExecutionId),
    ];

    public static string Filter(bool postgreSql)
    {
        return postgreSql
            ? $"\"{nameof(TokenUsage.IsInTotals)}\" = false"
            : $"[{nameof(TokenUsage.IsInTotals)}] = 0";
    }
}
