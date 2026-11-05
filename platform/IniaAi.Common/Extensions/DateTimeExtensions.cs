namespace IniaAi.Common;

public static class DateTimeExtensions
{
    /// <summary>
    /// Returns UTC midnight on the first day of the value's month.
    /// </summary>
    public static DateTime ToMonthStart(this DateTime date)
    {
        return new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Returns midnight on the last day of the value's month, preserving the DateTimeKind.
    /// </summary>
    public static DateTime ToMonthEnd(this DateTime date)
    {
        return new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month), 0, 0, 0, date.Kind);
    }
}
