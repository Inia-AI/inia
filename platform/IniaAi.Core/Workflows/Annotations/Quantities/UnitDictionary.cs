using System.Reflection;
using System.Text.Json;
using NReco.Text;

namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// Loads the CLDR-derived units dictionary into an Aho-Corasick double-array
/// trie keyed by surface form, with each entry tagged with its
/// <see cref="UnitKind"/> and optional subtype (ISO currency code or CLDR
/// unit-id).
///
/// Augmentations applied at load time:
///   1. ISO currency codes ("USD", "EUR", ...) are added as searchable surfaces
///      (CLDR ships them as keys, not as names).
///   2. Magnitude scalars ("million", "billion", "k", "M", "B", "trillion", ...)
///      are added as separate <see cref="UnitKind.Magnitude"/> entries so the
///      window-pairing pass can multiply numeric values when one is found.
///   3. Per-entry word-boundary flags are inferred for short surfaces (length 1
///      or 2) — they only fire when adjacent to a numeric span, never inside a
///      longer word.
///
/// Thread-safe singleton, built once on first access via <see cref="Lazy{T}"/>.
/// The lexicon JSON is loaded as an embedded resource from this assembly.
/// </summary>
internal sealed class UnitDictionary
{
    private const string EMBEDDED_RESOURCE_NAME =
        "IniaAi.Core.Workflows.Annotations.Quantities.Resources.units_dictionary.json";

    private const string CURRENCIES_KEY = "currencies";
    private const string UNITS_KEY = "units";
    private const string NAMES_KEY = "names";
    private const string CATEGORY_KEY = "category";
    private const string SYMBOL_KEY = "symbol";

    /// <summary>
    /// Surfaces that exist in CLDR as unit narrow-forms but produce far more
    /// false positives than true positives in our financial/legal/research text
    /// distribution. <c>"in"</c> is CLDR's narrow form for length-inch but is
    /// vastly more often the English preposition (<c>"$300 billion in revenue"</c>,
    /// <c>"61 billion in assets"</c>). Filtered at trie build so the matcher
    /// never sees them.
    /// </summary>
    private static readonly HashSet<string> UNIT_SURFACE_STOPWORDS = new(StringComparer.OrdinalIgnoreCase)
    {
        "in",
    };

    private static readonly Lazy<UnitDictionary> _instance =
        new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    public static UnitDictionary Instance => _instance.Value;

    public AhoCorasickDoubleArrayTrie<UnitEntry> Trie { get; }
    public IReadOnlyList<UnitEntry> Entries { get; }

    private UnitDictionary(
        AhoCorasickDoubleArrayTrie<UnitEntry> trie,
        IReadOnlyList<UnitEntry> entries)
    {
        Trie = trie;
        Entries = entries;
    }

    private static UnitDictionary LoadEmbedded()
    {
        Assembly assembly = typeof(UnitDictionary).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(EMBEDDED_RESOURCE_NAME)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{EMBEDDED_RESOURCE_NAME}' not found in {assembly.FullName}");

        using var doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;

        var entries = new List<UnitEntry>();
        // Order matters for dedup: lower-priority sources first, higher-priority
        // last. With LAST-write-wins on case-insensitive surfaces, this means
        // Magnitudes override CLDR unit narrow-forms ("M" beats duration-minute
        // "m"), and SupplementalLexicon overrides everything earlier.
        AddUnits(root, entries);
        AddCurrencies(root, entries);
        AddMagnitudeScalars(entries);
        entries.AddRange(SupplementalLexicon.GetEntries());

        var surfaceToEntry = new Dictionary<string, UnitEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (UnitEntry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Surface))
            {
                continue;
            }

            // Last-write-wins: priority is encoded in the order entries are
            // added above. CLDR's narrow form "m" for duration-minute is a
            // genuine collision in financial text where "754M" means 754
            // million; we want Magnitude to win that collision, hence Magnitude
            // is added after Units.
            surfaceToEntry[entry.Surface] = entry;
        }

        var trie = new AhoCorasickDoubleArrayTrie<UnitEntry>(surfaceToEntry, ignoreCase: true);
        return new UnitDictionary(trie, entries);
    }

    private static void AddCurrencies(JsonElement root, List<UnitEntry> sink)
    {
        if (!root.TryGetProperty(CURRENCIES_KEY, out JsonElement currencies))
        {
            return;
        }

        foreach (JsonProperty currency in currencies.EnumerateObject())
        {
            string isoCode = currency.Name;
            JsonElement entry = currency.Value;

            if (entry.TryGetProperty(NAMES_KEY, out JsonElement names))
            {
                foreach (JsonElement name in names.EnumerateArray())
                {
                    string? surface = name.GetString();
                    if (string.IsNullOrWhiteSpace(surface))
                    {
                        continue;
                    }

                    sink.Add(new UnitEntry(
                        Surface: surface,
                        Kind: UnitKind.Currency,
                        Subtype: isoCode,
                        Position: UnitPosition.Suffix,
                        RequiresWordBoundary: true));
                }
            }

            if (entry.TryGetProperty(SYMBOL_KEY, out JsonElement symbolElement))
            {
                string? symbol = symbolElement.GetString();
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    sink.Add(new UnitEntry(
                        Surface: symbol,
                        Kind: UnitKind.Currency,
                        Subtype: isoCode,
                        Position: UnitPosition.Prefix,
                        RequiresWordBoundary: false));
                }
            }

            // ISO code itself ("USD", "EUR", "GBP"). CLDR omits these from names
            // but financial text loves them. Treat as either-side, word-boundary
            // required (otherwise USD matches "fUSDe").
            sink.Add(new UnitEntry(
                Surface: isoCode,
                Kind: UnitKind.Currency,
                Subtype: isoCode,
                Position: UnitPosition.Either,
                RequiresWordBoundary: true));
        }
    }

    private static void AddUnits(JsonElement root, List<UnitEntry> sink)
    {
        if (!root.TryGetProperty(UNITS_KEY, out JsonElement units))
        {
            return;
        }

        foreach (JsonProperty unit in units.EnumerateObject())
        {
            string unitId = unit.Name;
            JsonElement entry = unit.Value;

            UnitKind kind = ParseKindFromCategory(
                entry.TryGetProperty(CATEGORY_KEY, out JsonElement cat) ? cat.GetString() : null);

            if (!entry.TryGetProperty(NAMES_KEY, out JsonElement names))
            {
                continue;
            }

            foreach (JsonElement name in names.EnumerateArray())
            {
                string? surface = name.GetString();
                if (string.IsNullOrWhiteSpace(surface))
                {
                    continue;
                }

                if (UNIT_SURFACE_STOPWORDS.Contains(surface))
                {
                    continue;
                }

                bool isShort = surface.Length <= 2;
                sink.Add(new UnitEntry(
                    Surface: surface,
                    Kind: kind,
                    Subtype: unitId,
                    Position: UnitPosition.Suffix,
                    RequiresWordBoundary: isShort));
            }
        }
    }

    private static void AddMagnitudeScalars(List<UnitEntry> sink)
    {
        // All Magnitude entries use Position.Either since they're scalars, not
        // units. Bare single letters (M, B, K, T) are hugely ambiguous against
        // CLDR narrow forms (duration-minute "m", digital-byte "B", etc.) but
        // in our financial-text distribution the magnitude reading dominates.
        // Word-boundary requirement keeps them from matching inside words like
        // "AES" or "Mbit/s".
        (string Surface, double Factor)[] scalars =
        [
            ("trillion", 1e12),
            ("trillions", 1e12),
            ("billion", 1e9),
            ("billions", 1e9),
            ("million", 1e6),
            ("millions", 1e6),
            ("thousand", 1e3),
            ("thousands", 1e3),
            ("hundred", 1e2),
            ("hundreds", 1e2),
            ("bn", 1e9),
            ("bln", 1e9),
            ("mn", 1e6),
            ("mln", 1e6),
            ("Bn", 1e9),
            ("Mn", 1e6),
            ("M", 1e6),
            ("B", 1e9),
            ("K", 1e3),
            ("T", 1e12),
        ];

        foreach ((string surface, double factor) in scalars)
        {
            sink.Add(new UnitEntry(
                Surface: surface,
                Kind: UnitKind.Magnitude,
                Subtype: null,
                Position: UnitPosition.Either,
                RequiresWordBoundary: true,
                MagnitudeFactor: factor));
        }
    }

    private static UnitKind ParseKindFromCategory(string? category)
    {
        return category switch
        {
            "acceleration" => UnitKind.Acceleration,
            "angle" => UnitKind.Angle,
            "area" => UnitKind.Area,
            "concentr" => UnitKind.Concentration,
            "consumption" => UnitKind.Consumption,
            "digital" => UnitKind.Digital,
            "duration" => UnitKind.Duration,
            "electric" => UnitKind.Electric,
            "energy" => UnitKind.Energy,
            "force" => UnitKind.Force,
            "frequency" => UnitKind.Frequency,
            "graphics" => UnitKind.Graphics,
            "length" => UnitKind.Length,
            "light" => UnitKind.Light,
            "mass" => UnitKind.Mass,
            "power" => UnitKind.Power,
            "pressure" => UnitKind.Pressure,
            "speed" => UnitKind.Speed,
            "temperature" => UnitKind.Temperature,
            "torque" => UnitKind.Torque,
            "volume" => UnitKind.Volume,
            _ => UnitKind.Plain,
        };
    }
}
