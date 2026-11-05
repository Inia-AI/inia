namespace IniaAi.Common.Numerics;

/// <summary>
/// Canonical magnitude suffix parsing: "billion" → 1e9, "M" → 1e6, etc.
/// Consolidates duplicate multiplier logic used across the codebase.
/// </summary>
public static class MagnitudeParser
{
    private const string UNITS = "units";

    /// <summary>
    /// Suffix→factor pairs ordered longest-first for regex/EndsWith matching.
    /// Word suffixes include a leading space for boundary detection; short suffixes
    /// are listed in both cases (M/m, B/b, etc.) for case-sensitive regex patterns.
    /// </summary>
    public static readonly (string Suffix, double Factor)[] SuffixFactors =
    [
        (" trillion", 1e12), (" billion", 1e9), (" million", 1e6), (" thousand", 1e3),
        ("Mn", 1e6), ("Bn", 1e9), ("mn", 1e6), ("bn", 1e9),
        ("M", 1e6), ("B", 1e9), ("K", 1e3), ("m", 1e6), ("b", 1e9), ("k", 1e3)
    ];

    private static readonly Dictionary<string, double> Lookup =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["trillion"] = 1e12,
            ["t"] = 1e12,
            ["billion"] = 1e9,
            ["billions"] = 1e9,
            ["b"] = 1e9,
            ["bn"] = 1e9,
            ["bln"] = 1e9,
            ["million"] = 1e6,
            ["millions"] = 1e6,
            ["m"] = 1e6,
            ["mm"] = 1e6,
            ["mn"] = 1e6,
            ["mln"] = 1e6,
            ["thousand"] = 1e3,
            ["thousands"] = 1e3,
            ["k"] = 1e3,
            ["th"] = 1e3,
        };

    /// <summary>
    /// Parses a magnitude suffix string to its multiplier. Case-insensitive.
    /// Returns 1.0 for null, empty, "units", or unrecognized suffixes.
    /// </summary>
    public static double GetMultiplier(string? suffix)
    {
        return TryGetMultiplier(suffix, out double factor)
            ? factor
            : 1;
    }

    /// <summary>
    /// Like <see cref="GetMultiplier"/>, but tells a recognised word from an unknown one:
    /// true for "units" (factor 1) and for every magnitude word, false for null, empty or
    /// unrecognised input, in which case <paramref name="factor"/> carries no meaning.
    /// </summary>
    public static bool TryGetMultiplier(string? suffix, out double factor)
    {
        factor = 1;

        if (string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        string trimmed = suffix.Trim();

        return string.Equals(trimmed, UNITS, StringComparison.OrdinalIgnoreCase)
            || Lookup.TryGetValue(trimmed, out factor);
    }

    /// <summary>
    /// Same as <see cref="GetMultiplier"/> but returns <see cref="decimal"/>.
    /// </summary>
    public static decimal GetMultiplierAsDecimal(string? suffix)
    {
        return (decimal)GetMultiplier(suffix);
    }
}
