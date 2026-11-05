using System.Globalization;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Workflows.Annotations.Dates;

/// <summary>
/// Reads a date from the forms documents actually write, without a general-purpose parser.
/// The BCL parser cannot be used here: it fills components it was not given from the current
/// date, so "March 15" would resolve against whichever year the run happened to execute in and
/// a stored run would stop reproducing. Every pattern is anchored and every digit class is
/// ASCII, so a capture always parses and no value can throw. An all-numeric value whose
/// ordering is genuinely ambiguous keeps both readings rather than guessing one.
/// </summary>
public static partial class DateNormalization
{
    private const int SHORTEST_FULL_YEAR = 4;
    private const int MONTHS_IN_YEAR = 12;

    private static readonly string[] _monthNames =
    [
        "january", "february", "march", "april", "may", "june",
        "july", "august", "september", "october", "november", "december",
    ];

    public static DateReading Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DateReading(DateReadOutcome.NotADate, null);
        }

        if (IsoRegex().Match(text) is { Success: true } iso)
        {
            return FromParts(Number(iso.Groups[1]), Number(iso.Groups[2]), Number(iso.Groups[3]));
        }

        if (NumericTripleRegex().Match(text) is { Success: true } triple)
        {
            return FromNumericTriple(triple);
        }

        if (DayMonthNameYearRegex().Match(text) is { Success: true } dmy)
        {
            return FromNamedMonth(dmy.Groups[3], dmy.Groups[2], dmy.Groups[1]);
        }

        if (MonthNameDayYearRegex().Match(text) is { Success: true } mdy)
        {
            return FromNamedMonth(mdy.Groups[3], mdy.Groups[1], mdy.Groups[2]);
        }

        if (MonthNameYearRegex().Match(text) is { Success: true } my)
        {
            return FromNamedMonth(my.Groups[2], my.Groups[1], null);
        }

        if (IsoMonthRegex().Match(text) is { Success: true } isoMonth)
        {
            return FromParts(Number(isoMonth.Groups[1]), Number(isoMonth.Groups[2]), null);
        }

        return new DateReading(DateReadOutcome.NotADate, null);
    }

    private static DateReading FromNumericTriple(Match triple)
    {
        Group first = triple.Groups[1];
        Group middle = triple.Groups[2];
        Group last = triple.Groups[3];

        if (first.Length >= SHORTEST_FULL_YEAR)
        {
            return FromParts(Number(first), Number(middle), Number(last));
        }

        if (last.Length != SHORTEST_FULL_YEAR)
        {
            return Unreadable();
        }

        int year = Number(last);
        DateOnly? dayFirst = TryBuild(year, Number(middle), Number(first));
        DateOnly? monthFirst = TryBuild(year, Number(first), Number(middle));

        if (dayFirst is null && monthFirst is null)
        {
            return Unreadable();
        }

        if (dayFirst is null || monthFirst is null || dayFirst == monthFirst)
        {
            return Read(DatePrecision.Day, (monthFirst ?? dayFirst)!.Value, null);
        }

        return Read(DatePrecision.Day, monthFirst.Value, dayFirst.Value);
    }

    private static DateReading FromNamedMonth(Group year, Group monthName, Group? day)
    {
        int month = Array.FindIndex(
            _monthNames,
            n => n.StartsWith(monthName.Value.ToLowerInvariant(), StringComparison.Ordinal)) + 1;

        if (month == 0 || year.Length != SHORTEST_FULL_YEAR)
        {
            return Unreadable();
        }

        return FromParts(Number(year), month, day is null ? null : Number(day));
    }

    private static DateReading FromParts(int year, int month, int? day)
    {
        if (day is null)
        {
            return month is < 1 or > MONTHS_IN_YEAR
                ? Unreadable()
                : Read(DatePrecision.Month, new DateOnly(year, month, 1), null);
        }

        return TryBuild(year, month, day.Value) is DateOnly built
            ? Read(DatePrecision.Day, built, null)
            : Unreadable();
    }

    private static DateOnly? TryBuild(int year, int month, int day)
    {
        if (month is < 1 or > MONTHS_IN_YEAR || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        return new DateOnly(year, month, day);
    }

    private static DateReading Read(
        DatePrecision precision,
        DateOnly reading,
        DateOnly? alternate)
    {
        return new DateReading(
            DateReadOutcome.Read,
            new NormalizedDate(precision, reading, alternate));
    }

    private static DateReading Unreadable()
    {
        return new DateReading(DateReadOutcome.ShapedButUnreadable, null);
    }

    private static int Number(Group digits)
    {
        return int.Parse(digits.ValueSpan, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^\s*([0-9]{4})-([0-9]{1,2})-([0-9]{1,2})(?:[T ].*)?$")]
    private static partial Regex IsoRegex();

    [GeneratedRegex(@"^\s*([0-9]{1,4})[./-]([0-9]{1,2})[./-]([0-9]{1,4})\s*$")]
    private static partial Regex NumericTripleRegex();

    [GeneratedRegex(@"^\s*([0-9]{1,2})(?:st|nd|rd|th)?[\s./-]+(?:of\s+)?([A-Za-z]{3,9})\.?,?[\s./-]+([0-9]{2,4})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DayMonthNameYearRegex();

    [GeneratedRegex(@"^\s*([A-Za-z]{3,9})\.?[\s./-]+([0-9]{1,2})(?:st|nd|rd|th)?,?[\s./-]+([0-9]{2,4})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MonthNameDayYearRegex();

    [GeneratedRegex(@"^\s*([A-Za-z]{3,9})\.?,?[\s./-]+([0-9]{2,4})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MonthNameYearRegex();

    [GeneratedRegex(@"^\s*([0-9]{4})-([0-9]{1,2})\s*$")]
    private static partial Regex IsoMonthRegex();
}
