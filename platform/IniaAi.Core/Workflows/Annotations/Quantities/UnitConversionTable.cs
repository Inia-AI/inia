using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// Loads the CLDR-derived conversion data (generated and pinned by
/// <c>generate_unit_conversions.py</c>; the CLDR version is recorded in the
/// resource's <c>_meta</c>) keyed by the subtype ids that
/// <see cref="UnitDictionary"/> and <see cref="SupplementalLexicon"/> emit.
/// A subtype without an entry is non-convertible: cross-subtype pairs that
/// miss the table never match, so an unknown unit can never be compared on a
/// silently wrong scale.
///
/// Thread-safe singleton, built once on first access via <see cref="Lazy{T}"/>.
/// </summary>
internal sealed class UnitConversionTable
{
    private const string EMBEDDED_RESOURCE_NAME =
        "IniaAi.Core.Workflows.Annotations.Quantities.Resources.unit_conversions.json";

    private const string CONVERSIONS_KEY = "conversions";
    private const string DIMENSION_KEY = "dimension";
    private const string FACTOR_NUMERATOR_KEY = "factorNum";
    private const string FACTOR_DENOMINATOR_KEY = "factorDen";
    private const string OFFSET_NUMERATOR_KEY = "offsetNum";
    private const string OFFSET_DENOMINATOR_KEY = "offsetDen";
    private const string CURRENCY_KEY = "currency";

    private static readonly Lazy<UnitConversionTable> _instance =
        new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IReadOnlyDictionary<string, UnitConversionEntry> _entries;

    private UnitConversionTable(IReadOnlyDictionary<string, UnitConversionEntry> entries)
    {
        _entries = entries;
    }

    public static UnitConversionTable Instance => _instance.Value;

    public int Count => _entries.Count;

    public bool TryGet(string? subtype, [NotNullWhen(true)] out UnitConversionEntry? entry)
    {
        entry = null;

        return subtype is not null && _entries.TryGetValue(subtype, out entry);
    }

    private static UnitConversionTable LoadEmbedded()
    {
        Assembly assembly = typeof(UnitConversionTable).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(EMBEDDED_RESOURCE_NAME)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EMBEDDED_RESOURCE_NAME}' not found.");
        using JsonDocument document = JsonDocument.Parse(stream);

        Dictionary<string, UnitConversionEntry> entries = new(StringComparer.Ordinal);

        foreach (JsonProperty conversion in document.RootElement.GetProperty(CONVERSIONS_KEY).EnumerateObject())
        {
            JsonElement value = conversion.Value;

            entries.Add(
                conversion.Name,
                new UnitConversionEntry(
                    Dimension: value.GetProperty(DIMENSION_KEY).GetString()
                        ?? throw new InvalidOperationException(
                            $"Conversion entry '{conversion.Name}' has no dimension."),
                    FactorNumerator: ReadRational(value, FACTOR_NUMERATOR_KEY, conversion.Name),
                    FactorDenominator: ReadRational(value, FACTOR_DENOMINATOR_KEY, conversion.Name),
                    OffsetNumerator: ReadOptionalRational(value, OFFSET_NUMERATOR_KEY, 0m, conversion.Name),
                    OffsetDenominator: ReadOptionalRational(value, OFFSET_DENOMINATOR_KEY, 1m, conversion.Name),
                    CurrencyCode: value.TryGetProperty(CURRENCY_KEY, out JsonElement currency)
                        ? currency.GetString()
                        : null));
        }

        return new UnitConversionTable(entries);
    }

    /// <summary>
    /// The generator writes Python rationals, which normalize a negative sign
    /// onto the numerator — a CLDR tag with a negative offset must load, not
    /// poison the lazy singleton with a cached FormatException.
    /// </summary>
    internal static decimal ReadRational(JsonElement entry, string key, string subtype)
    {
        string text = entry.GetProperty(key).GetString()
            ?? throw new InvalidOperationException($"Conversion entry '{subtype}' has a non-string '{key}'.");

        return decimal.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static decimal ReadOptionalRational(JsonElement entry, string key, decimal fallback, string subtype)
    {
        return entry.TryGetProperty(key, out _) ? ReadRational(entry, key, subtype) : fallback;
    }
}
