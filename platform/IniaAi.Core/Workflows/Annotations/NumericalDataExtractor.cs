using System.Text.RegularExpressions;
using IniaAi.Common.Numerics;
using IniaAi.Core.Workflows.Annotations.Quantities;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// Extracts numerical data (dates, currencies, percentages, plain numbers) from text.
/// Uses a consume-and-exclude strategy: dates are matched first, then currencies,
/// then percentages, then plain numbers. Positions consumed by earlier patterns
/// are excluded from later ones, preventing double extraction.
/// </summary>
public static partial class NumericalDataExtractor
{
    /// <summary>
    /// Multiplier suffix definitions used by currency parsing and suffix stripping.
    /// Delegates to <see cref="MagnitudeParser.SuffixFactors"/> for the canonical definitions.
    /// </summary>
    internal static (string Suffix, double Factor)[] MultiplierSuffixes => MagnitudeParser.SuffixFactors;

    public static List<string> Extract(string? text)
    {
        return [.. ExtractPositions(text).Select(found => found.Value)];
    }

    /// <summary>
    /// Every numerical span in <paramref name="text"/> with its start position, in text order.
    /// </summary>
    public static List<(int Position, string Value)> ExtractPositions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // A scale header's zeros ("£'000") are no figure, so no phase may take them.
        List<(int Start, int End)> consumed = ScaleHeader.ZeroHeaderSpans(text);
        var results = new List<(int Position, string Value)>();

        // Phase 1: Dates (highest priority — consume first)
        ExtractDates(text, consumed, results);

        // Phase 2: Currencies
        ExtractCurrencies(text, consumed, results);

        // Phase 3: Percentages, basis points, percentage points
        ExtractPercentages(text, consumed, results);

        // Phase 4: Plain numbers (lowest priority)
        ExtractPlainNumbers(text, consumed, results);

        results.Sort((a, b) => a.Position.CompareTo(b.Position));
        return results;
    }

    /// <summary>
    /// The date phase alone: every date-shaped span in <paramref name="text"/>, in text order.
    /// </summary>
    public static List<string> ExtractDates(string? text)
    {
        return ValuesInTextOrder(ExtractDatePositions(text));
    }

    /// <summary>
    /// The date phase alone, with each span's start position so a caller can wrap it.
    /// </summary>
    public static List<(int Position, string Value)> ExtractDatePositions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var consumed = new List<(int Start, int End)>();
        var results = new List<(int Position, string Value)>();

        ExtractDates(text, consumed, results);

        return results;
    }

    private static List<string> ValuesInTextOrder(List<(int Position, string Value)> results)
    {
        results.Sort((a, b) => a.Position.CompareTo(b.Position));

        return [.. results.Select(r => r.Value)];
    }

    private static void ExtractDates(
        string text,
        List<(int Start, int End)> consumed,
        List<(int Position, string Value)> results)
    {
        // Order: more specific patterns first
        ApplyPattern(MonthDayYearRegex(), text, consumed, results);
        ApplyPattern(DayMonthYearRegex(), text, consumed, results);
        ApplyPattern(IsoDateRegex(), text, consumed, results);
        ApplyPattern(IsoYearMonthRegex(), text, consumed, results);
        ApplyPattern(UsDateRegex(), text, consumed, results);
        ApplyPattern(YearRangeRegex(), text, consumed, results);
        ApplyPattern(QuarterRegex(), text, consumed, results);
        ApplyPattern(HalfRegex(), text, consumed, results);
        ApplyPattern(FiscalYearRegex(), text, consumed, results);
        ApplyPattern(CalendarYearRegex(), text, consumed, results);
        ApplyPattern(MonthYearRegex(), text, consumed, results);
    }

    private static void ExtractCurrencies(
        string text,
        List<(int Start, int End)> consumed,
        List<(int Position, string Value)> results)
    {
        ApplyPattern(CurrencySymbolRegex(), text, consumed, results);
        ApplyPattern(CurrencyCodeRegex(), text, consumed, results);
    }

    private static void ExtractPercentages(
        string text,
        List<(int Start, int End)> consumed,
        List<(int Position, string Value)> results)
    {
        ApplyPattern(BasisPointsRegex(), text, consumed, results);
        ApplyPattern(BasisPointsShortRegex(), text, consumed, results);
        ApplyPattern(PercentagePointsRegex(), text, consumed, results);
        ApplyPattern(PercentagePointsShortRegex(), text, consumed, results);
        // Range form ("13-14%", "12.4-12.43%") runs before PercentSymbolRegex so
        // its leading "-?" never consumes the range dash as a sign — that bug
        // was emitting "-14%" for "13-14%" and dropping the lower bound.
        ApplyPercentRangePattern(text, consumed, results);
        ApplyPattern(PercentSpelledRegex(), text, consumed, results);
        ApplyPattern(PercentSymbolRegex(), text, consumed, results);
    }

    /// <summary>
    /// Custom handler for <see cref="PercentRangeRegex"/>: emits the lower and
    /// upper bounds as separate <c>N%</c> results and marks the whole range
    /// span consumed so neither <see cref="PercentSymbolRegex"/> nor the plain-
    /// number regexes can re-extract part of it.
    /// </summary>
    private static void ApplyPercentRangePattern(
        string text,
        List<(int Start, int End)> consumed,
        List<(int Position, string Value)> results)
    {
        foreach (Match match in PercentRangeRegex().Matches(text))
        {
            int start = match.Index;
            int end = start + match.Length;

            if (Overlaps(start, end, consumed))
            {
                continue;
            }

            Group lower = match.Groups[1];
            Group upper = match.Groups[2];
            consumed.Add((start, end));
            results.Add((lower.Index, lower.Value + "%"));
            results.Add((upper.Index, upper.Value + "%"));
        }
    }

    private static void ExtractPlainNumbers(
        string text,
        List<(int Start, int End)> consumed,
        List<(int Position, string Value)> results)
    {
        ApplyPattern(NumberMultiplierRegex(), text, consumed, results);
        ApplyPattern(CommaNumberRegex(), text, consumed, results);
        ApplyPattern(PlainNumberRegex(), text, consumed, results);
    }

    private static void ApplyPattern(
        Regex regex,
        string text,
        List<(int Start, int End)> consumed,
        List<(int Position, string Value)> results)
    {
        foreach (Match match in regex.Matches(text))
        {
            int start = match.Index;
            int end = start + match.Length;

            if (Overlaps(start, end, consumed))
            {
                continue;
            }

            consumed.Add((start, end));
            results.Add((start, match.Value));
        }
    }

    internal static bool Overlaps(int start, int end, List<(int Start, int End)> consumed)
    {
        foreach ((int cStart, int cEnd) in consumed)
        {
            if (start < cEnd && end > cStart)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"(?:January|February|March|April|May|June|July|August|September|October|November|December|Jan\.?|Feb\.?|Mar\.?|Apr\.?|May|Jun\.?|Jul\.?|Aug\.?|Sep\.?|Oct\.?|Nov\.?|Dec\.?)\.?\s+\d{1,2},?\s+\d{4}")]
    private static partial Regex MonthDayYearRegex();

    // UK / international "31 December 2024", "1 Jan 2025", "31st December 2024".
    // Runs before MonthYearRegex so the day isn't stranded as a separate plain number.
    [GeneratedRegex(@"\d{1,2}(?:st|nd|rd|th)?\s+(?:January|February|March|April|May|June|July|August|September|October|November|December|Jan\.?|Feb\.?|Mar\.?|Apr\.?|May|Jun\.?|Jul\.?|Aug\.?|Sep\.?|Oct\.?|Nov\.?|Dec\.?)\.?,?\s+\d{4}")]
    private static partial Regex DayMonthYearRegex();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDateRegex();

    /// <summary>
    /// A month written "2024-07". A two-year span such as "2024-25" ends past twelve from 2012 on,
    /// so it is never read as a month; an earlier span such as "2010-11" is.
    /// </summary>
    [GeneratedRegex(@"(?<![\d-])\d{4}-(?:0[1-9]|1[0-2])(?![\d-])")]
    private static partial Regex IsoYearMonthRegex();

    [GeneratedRegex(@"\d{2}/\d{2}/\d{4}")]
    private static partial Regex UsDateRegex();

    [GeneratedRegex(@"\d{4}[-\u2013]\d{4}")]
    private static partial Regex YearRangeRegex();

    [GeneratedRegex(@"Q[1-4]\s?\d{4}")]
    private static partial Regex QuarterRegex();

    [GeneratedRegex(@"H[12]\s?\d{4}")]
    private static partial Regex HalfRegex();

    [GeneratedRegex(@"FY\s?\d{4}")]
    private static partial Regex FiscalYearRegex();

    [GeneratedRegex(@"CY\s?\d{4}")]
    private static partial Regex CalendarYearRegex();

    [GeneratedRegex(@"(?:January|February|March|April|May|June|July|August|September|October|November|December|Jan\.?|Feb\.?|Mar\.?|Apr\.?|May|Jun\.?|Jul\.?|Aug\.?|Sep\.?|Oct\.?|Nov\.?|Dec\.?)\.?\s+\d{4}")]
    private static partial Regex MonthYearRegex();

    [GeneratedRegex(@"-?[\$\u20ac\u00a3\u00a5](?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?:(?:\s+(?:million|billion|trillion|thousand))|(?:[MBKmk]n?|bn|mn|[Tt](?:rillion|housand)|[Mm](?:illion)))?")]
    private static partial Regex CurrencySymbolRegex();

    [GeneratedRegex(@"(?:USD|EUR|GBP|JPY|CHF|CAD|AUD|NZD|CNY|HKD|SGD|SEK|NOK|DKK|ZAR|BRL|INR|KRW|MXN|TWD)\s+(?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?:(?:\s+(?:million|billion|trillion|thousand))|(?:[MBKmk]n?|bn|mn|[Tt](?:rillion|housand)|[Mm](?:illion)))?")]
    private static partial Regex CurrencyCodeRegex();

    [GeneratedRegex(@"-?(?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s+basis\s+points")]
    private static partial Regex BasisPointsRegex();

    [GeneratedRegex(@"-?(?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s+bps\b")]
    private static partial Regex BasisPointsShortRegex();

    [GeneratedRegex(@"-?(?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s+percentage\s+points")]
    private static partial Regex PercentagePointsRegex();

    [GeneratedRegex(@"-?(?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s+pp\b")]
    private static partial Regex PercentagePointsShortRegex();

    [GeneratedRegex(@"-?(?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s+percent\b")]
    private static partial Regex PercentSpelledRegex();

    [GeneratedRegex(@"-?(?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)%")]
    private static partial Regex PercentSymbolRegex();

    // Range form: "13-14%", "12.4-12.43%", "6.26-6.263%". Two numeric groups
    // separated by a hyphen or en-dash, with a single trailing "%". Both bounds
    // share the same percent unit. Captured as groups so the handler can emit
    // each bound as its own "N%" result instead of treating the dash as a sign.
    [GeneratedRegex(@"((?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?))[-–]((?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?))%")]
    private static partial Regex PercentRangeRegex();

    [GeneratedRegex(@"-?(?:\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)x\b")]
    private static partial Regex NumberMultiplierRegex();

    [GeneratedRegex(@"(?<!\d)\d{1,3}(?:,\d{3})+(?:\.\d+)?(?!\d)")]
    private static partial Regex CommaNumberRegex();

    [GeneratedRegex(@"(?<![,.\d]|[\$\u20ac\u00a3\u00a5])(?:\d+\.\d+|\d{2,})(?![,.]?\d|%|x\b)")]
    private static partial Regex PlainNumberRegex();
}
