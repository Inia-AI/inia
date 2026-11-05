using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace IniaAi.Core.Services;

public partial class JsonStringReplacerStrategy : IStringReplacerStrategy
{
    // Matches both single braces {placeholder} and double braces {{placeholder}}
    // Double braces are matched first (greedy), then single braces
    private static readonly Regex _embeddedPlaceholderPattern = GenerateEmbeddedPlaceholderRegex();

    // Parses a single dot-delimited segment into a property key and zero or more
    // non-negative integer indices. The regex itself permits an empty key (for
    // bare-bracket segments like "[0]"), but ResolveSinglePath rejects those
    // afterwards so path authors can only write one spelling per path
    // ("foo[0]", never "foo.[0]"). Examples:
    //   "choices"       => key="choices",  indices=""
    //   "choices[0]"    => key="choices",  indices="[0]"
    //   "matrix[0][1]"  => key="matrix",   indices="[0][1]"
    private static readonly Regex _segmentPattern = GenerateSegmentPatternRegex();

    private static readonly Regex _indexPattern = GenerateIndexPatternRegex();

    /// <summary>
    /// How a placeholder renders a resolved JSON object or array. Letters of every script in the
    /// Basic Multilingual Plane stay as written, so Arabic or Cyrillic text reaches a prompt readable
    /// and without \u bloat. Characters outside that plane (emoji, rare CJK) stay escaped, as the
    /// encoder always escapes them. HTML-sensitive characters (&lt; &gt; &amp; '), controls and every
    /// invisible format character (bidi marks, overrides and isolates, zero-width characters) stay
    /// escaped, so no character that was escaped before and can change how text displays becomes raw.
    /// </summary>
    private static readonly JsonSerializerOptions _placeholderRenderOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(PlaceholderTextEncoderSettings()),
    };

    public static readonly string DefaultNoValue = "No data available";

    // Part of how Persian, Urdu and Indic words are spelled, so they stay raw like the letters.
    private const int ZERO_WIDTH_NON_JOINER = 0x200C;
    private const int ZERO_WIDTH_JOINER = 0x200D;

    private static TextEncoderSettings PlaceholderTextEncoderSettings()
    {
        var settings = new TextEncoderSettings(UnicodeRanges.All);
        for (int code = 0; code <= char.MaxValue; code++)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(code) == UnicodeCategory.Format
                && code is not ZERO_WIDTH_NON_JOINER and not ZERO_WIDTH_JOINER)
            {
                settings.ForbidCharacter((char)code);
            }
        }

        return settings;
    }

    public string ReplacePlaceholders(string input, string? jsonData, bool embeddedMode)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        JsonNode? root;
        try
        {
            root = !string.IsNullOrEmpty(jsonData) ? JsonNode.Parse(jsonData) : null;
        }
        catch
        {
            root = null;
        }

        // Reject primitive roots (number, string, boolean) per TERM-990, but allow object
        // AND array roots. A top-level array is what many external APIs return (e.g.
        // Financial Modeling Prep: "[ { ... } ]"); ResolveSinglePath unwraps/indexes it.
        if (root is not null and not JsonObject and not JsonArray)
        {
            root = null;
        }

        if (!embeddedMode)
        {
            if (root is null)
            {
                return DefaultNoValue;
            }

            // Single path, no braces
            string? replaced = ResolveSinglePath(input, root);
            return replaced ?? DefaultNoValue;
        }
        else
        {
            // Multiple placeholders in a single string
            return _embeddedPlaceholderPattern.Replace(input, match =>
            {
                string rawPath = match.Groups["path"].Value.Trim();

                if (root is null)
                {
                    return DefaultNoValue;
                }

                string? replacedVal = ResolveSinglePath(rawPath, root);
                // If we found a single value, return it, else keep original placeholder
                return replacedVal ?? DefaultNoValue;
            });
        }
    }

    private static string? ResolveSinglePath(string rawPath, JsonNode root)
    {
        if (root is null)
        {
            return DefaultNoValue;
        }

        string[] segments = rawPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 1)
        {
            return null;
        }

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = segments[i].Trim();
        }

        JsonNode? currentNode = root;
        int startIndex = 0;

        // Root-array handling. A leading bare-index segment ("[0]") targets the root
        // array directly; otherwise a single-element array auto-unwraps to its only
        // element so callers can write {tag.field} against an "[ { ... } ]" response.
        // A multi-element array with no leading index is ambiguous and rejected.
        // Non-leading bare-index segments stay invalid (handled by the loop below).
        if (currentNode is JsonArray rootArray)
        {
            Match firstSeg = _segmentPattern.Match(segments[0]);
            if (firstSeg.Success
                && string.IsNullOrEmpty(firstSeg.Groups["key"].Value)
                && !string.IsNullOrEmpty(firstSeg.Groups["idx"].Value))
            {
                currentNode = ApplyIndices(rootArray, firstSeg.Groups["idx"].Value);
                startIndex = 1;
            }
            else if (rootArray.Count == 1)
            {
                currentNode = rootArray[0];
            }
            else
            {
                return null;
            }

            if (currentNode is null)
            {
                return null;
            }
        }

        for (int s = startIndex; s < segments.Length; s++)
        {
            string seg = segments[s];
            if (string.IsNullOrWhiteSpace(seg))
            {
                return null;
            }

            Match segMatch = _segmentPattern.Match(seg);
            if (!segMatch.Success)
            {
                return null;
            }

            string key = segMatch.Groups["key"].Value;
            string indicesRaw = segMatch.Groups["idx"].Value;

            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (currentNode is not JsonObject obj)
            {
                return null;
            }

            currentNode = FindProperty(obj, key);
            if (currentNode is null)
            {
                return null;
            }

            currentNode = ApplyIndices(currentNode, indicesRaw);
            if (currentNode is null)
            {
                return null;
            }
        }

        return currentNode switch
        {
            JsonValue jv => jv.ToString(),
            JsonObject jo => jo.ToJsonString(_placeholderRenderOptions),
            JsonArray ja => ja.ToJsonString(_placeholderRenderOptions),
            _ => null,
        };
    }

    // Applies zero or more "[n]" indices (e.g. "[0][1]") to a node, descending into
    // arrays. Returns null on any non-array node, non-integer/overflowing index, or
    // out-of-bounds access. An empty indices string leaves the node unchanged.
    private static JsonNode? ApplyIndices(JsonNode? node, string indicesRaw)
    {
        foreach (Match idxMatch in _indexPattern.Matches(indicesRaw))
        {
            if (node is not JsonArray arr)
            {
                return null;
            }

            if (!int.TryParse(idxMatch.Groups[1].Value, out int idx))
            {
                return null;
            }

            if (idx >= arr.Count)
            {
                return null;
            }

            node = arr[idx];
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    // Exact match wins; otherwise the first case-insensitive match, so path authors need not
    // reproduce the exact casing of upstream JSON keys.
    private static JsonNode? FindProperty(JsonObject obj, string key)
    {
        if (obj.TryGetPropertyValue(key, out JsonNode? exact))
        {
            return exact;
        }

        foreach (KeyValuePair<string, JsonNode?> property in obj)
        {
            if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\{\{?\s*(?<path>.*?)\s*\}?\}", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex GenerateEmbeddedPlaceholderRegex();

    [GeneratedRegex(@"^(?<key>[^\[\]]*)(?<idx>(?:\[\d+\])*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex GenerateSegmentPatternRegex();

    [GeneratedRegex(@"\[(\d+)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex GenerateIndexPatternRegex();
}
