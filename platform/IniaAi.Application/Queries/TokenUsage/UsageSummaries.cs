using IniaAi.Common.Extensions;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.TokenUsage;

/// <summary>
/// Usage sums per UTC day and usage type: few enough rows to fetch whole, and enough to build the
/// period summaries, the usage-type breakdown and the daily trend in memory.
/// </summary>
public static class UsageSummaries
{
    public static async Task<List<UsageDayTypeSums>> ByDayAndTypeAsync(
        IQueryable<UsageDaySlice> slices,
        CancellationToken cancellationToken)
    {
        return await slices
            .GroupBy(s => new { s.Day, s.UsageType })
            .Select(g => new UsageDayTypeSums
            {
                Day = g.Key.Day,
                UsageType = g.Key.UsageType,
                InputTokens = g.Sum(s => s.InputTokens),
                OutputTokens = g.Sum(s => s.OutputTokens),
                CacheWriteTokens = g.Sum(s => s.CacheWriteTokens),
                CacheReadTokens = g.Sum(s => s.CacheReadTokens),
                EmbeddingTokens = g.Sum(s => s.EmbeddingTokens),
                Cost = g.Sum(s => s.Cost),
                OperationCount = g.Sum(s => s.OperationCount),
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Today, this week (from Sunday), this month and all time, each ending now. Every period starts
    /// at a UTC midnight, so a day's sums fall wholly inside or outside it.
    /// </summary>
    public static UsagePeriodSummaries Periods(IReadOnlyCollection<UsageDayTypeSums> sums, DateTime now)
    {
        DateTime todayStart = now.Date;
        DateTime weekStart = now.Date.AddDays(-(int)now.DayOfWeek);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return new UsagePeriodSummaries(
            Summary("Today", todayStart, now, sums),
            Summary("This Week", weekStart, now, sums),
            Summary("This Month", monthStart, now, sums),
            Summary("All Time", DateTime.MinValue, now, sums));
    }

    public static List<UsageByTypeDto> ByUsageType(IReadOnlyCollection<UsageDayTypeSums> sums)
    {
        long totalTokens = sums.Sum(s => s.Tokens);

        return [.. sums
            .GroupBy(s => s.UsageType)
            .Select(g => new UsageByTypeDto
            {
                UsageType = g.Key.ToString(),
                DisplayName = g.Key.GetDescription(),
                TotalTokens = g.Sum(s => s.Tokens),
                TotalCost = g.Sum(s => s.Cost),
                Percentage = Percentage(g.Sum(s => s.Tokens), totalTokens),
                OperationCount = g.Sum(s => s.OperationCount),
            })
            .OrderByDescending(b => b.TotalTokens)];
    }

    public static decimal Percentage(long tokens, long totalTokens)
    {
        return totalTokens > 0 ? Math.Round((decimal)tokens / totalTokens * 100, 1) : 0;
    }

    private static TokenUsageSummaryDto Summary(
        string period,
        DateTime periodStart,
        DateTime periodEnd,
        IReadOnlyCollection<UsageDayTypeSums> sums)
    {
        DateOnly firstDay = DateOnly.FromDateTime(periodStart);
        List<UsageDayTypeSums> inPeriod = [.. sums.Where(s => s.Day >= firstDay)];

        return new TokenUsageSummaryDto
        {
            Period = period,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            TotalInputTokens = inPeriod.Sum(s => s.InputTokens),
            TotalOutputTokens = inPeriod.Sum(s => s.OutputTokens),
            TotalTokens = inPeriod.Sum(s => s.Tokens),
            TotalCacheCreationTokens = inPeriod.Sum(s => s.CacheWriteTokens),
            TotalCacheReadTokens = inPeriod.Sum(s => s.CacheReadTokens),
            TotalCost = inPeriod.Sum(s => s.Cost),
            OperationCount = inPeriod.Sum(s => s.OperationCount),
        };
    }
}

public sealed class UsageDayTypeSums : UsageSums
{
    public DateOnly Day { get; init; }

    public UsageType UsageType { get; init; }

    /// <summary>
    /// A model call of any kind: every usage type in the band below the embeddings.
    /// </summary>
    public bool IsLlm => UsageType < UsageType.EmbeddingPage;

    /// <summary>
    /// A page, chunk or query embedding: the usage types from the embeddings' band up to reranking.
    /// </summary>
    public bool IsEmbedding => UsageType is >= UsageType.EmbeddingPage and < UsageType.Reranking;
}

public sealed record UsagePeriodSummaries(
    TokenUsageSummaryDto Today,
    TokenUsageSummaryDto ThisWeek,
    TokenUsageSummaryDto ThisMonth,
    TokenUsageSummaryDto AllTime);
