using IniaAi.Domain.Entities.DailyAllowances;

namespace IniaAi.Application.Common.DailyAllowances;

/// <param name="Used">Requests used today, this one included when it was taken.</param>
public sealed record DailyAllowanceTake(
    DailyAllowanceKind Kind,
    string UserId,
    DateOnly Day,
    int Limit,
    int Used,
    bool Taken)
{
    public int Remaining => Math.Max(0, Limit - Used);

    /// <summary>The next midnight UTC, when the allowance resets.</summary>
    public DateTime ResetsAt => MidnightAfter(Day);

    public static DateTime MidnightAfter(DateOnly day)
    {
        return day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }
}
