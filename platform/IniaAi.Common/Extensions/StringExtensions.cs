using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IniaAi.Common;

public static partial class StringExtensions
{
    private const string COPY_SUFFIX = " (copy)";

    /// <summary>
    /// Strips NUL (U+0000) characters, which PostgreSQL rejects in text and jsonb
    /// columns. Apply to externally ingested text (scraped pages, API response
    /// bodies, OCR output) before it reaches a persisted property.
    /// </summary>
    [return: NotNullIfNotNull(nameof(str))]
    public static string? RemoveNullCharacters(this string? str)
    {
        return str is null || !str.Contains('\0') ? str : str.Replace("\0", "");
    }

    public static IEnumerable<string> SplitCamelCase(this string text)
    {
        Regex regex = SPLIT_CAMEL_CASE();

        foreach (Match match in regex.Matches(text))
        {
            yield return match.Value;
        }
    }

    public static string FirstCharToUpper(this string text)
    {
        return text.First().ToString().ToUpper() + text[1..];
    }

    public static IEnumerable<string> GetPlaceholders(this string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        Regex regex = GET_PLACEHOLDERS();
        MatchCollection matches = regex.Matches(text);

        foreach (Match match in matches)
        {
            yield return match.Groups[1].Value;
        }
    }

    /// <summary>
    /// Reduces a placeholder to its lookup base tag the same way the runtime engine does
    /// (<c>WorkflowHandler</c>): the segment before the first dot, minus any <c>[index]</c>.
    /// So <c>PeerCompanies[0].Name</c> and <c>block_0.answer</c> become <c>PeerCompanies</c> and <c>block_0</c>.
    /// Note: only entity tags (e.g. PeerCompanies) actually resolve an <c>[index]</c> at runtime; a
    /// bracketed block tag like <c>block_0[1]</c> reduces to <c>block_0</c> here but the index is inert.
    /// </summary>
    public static string GetBaseReplacementTag(this string placeholder)
    {
        int dotIndex = placeholder.IndexOf('.');
        string baseTag = dotIndex >= 0 ? placeholder[..dotIndex] : placeholder;

        int bracketIndex = baseTag.IndexOf('[');
        return bracketIndex >= 0 ? baseTag[..bracketIndex] : baseTag;
    }

    public static string? ReplacePlaceholders(
        this string? str,
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison)
    {
        if (str is null)
        {
            return null;
        }

        if (asPlaceHolder && !str.Contains('{'))
        {
            return str;
        }

        foreach ((string placeholder, string? inputText) in inputs)
        {
            if (asPlaceHolder)
            {
                // Replace both double braces {{placeholder}} and single braces {placeholder}
                // Replace double braces first to avoid partial matches
                str = str.Replace($"{{{{{placeholder}}}}}", inputText ?? "", stringComparison);
                str = str.Replace($"{{{placeholder}}}", inputText ?? "", stringComparison);
            }
            else
            {
                str = str.Replace(placeholder, inputText ?? "", stringComparison);
            }
        }

        return str;
    }

    /// <summary>
    /// Like <see cref="ReplacePlaceholders"/>, but percent-encodes each substituted value
    /// (<see cref="Uri.EscapeDataString(string)"/>) so it cannot break the URL structure the
    /// template defines; the template's own literals stay raw, and a string that is one
    /// placeholder is encoded the same, so no value can carry a whole URL. A substituted value
    /// that itself contains input placeholders (a text input's value is a template too) is
    /// first resolved with plain replacement, then encoded as one composed value.
    /// </summary>
    public static string? ReplacePlaceholdersWithUrlEncodedValues(
        this string? str,
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison)
    {
        return str.ReplacePlaceholdersWithTransformedValues(
            asPlaceHolder, inputs, stringComparison, static (_, value) => UrlEncodeValue(value));
    }

    private static string UrlEncodeValue(string? value)
    {
        return string.IsNullOrEmpty(value) ? "" : Uri.EscapeDataString(value);
    }

    /// <summary>
    /// Like <see cref="ReplacePlaceholders"/>, but JSON-escapes each substituted value that lands
    /// inside a JSON string (<c>"…{tag}…"</c>) so it cannot break the string it sits in, while the
    /// template's own literals stay raw. A placeholder in bare value position (<c>"key": {tag}</c>),
    /// or one that is the whole string, substitutes verbatim — there the value is itself a JSON
    /// value (object/array/scalar) and escaping it would corrupt the structure. A value that is
    /// already valid escaped JSON string content also substitutes verbatim in string position
    /// (reported via <paramref name="onPreEscapedValueSkipped"/> with the placeholder name) so
    /// pre-escaped inputs are not double-escaped. A substituted value that itself contains input
    /// placeholders (a text input's value is a template too) is first resolved with plain
    /// replacement, then sanitized as one composed value.
    /// </summary>
    public static string? ReplacePlaceholdersWithJsonEscapedValues(
        this string? str,
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison,
        Action<string>? onPreEscapedValueSkipped = null)
    {
        if (str is null)
        {
            return null;
        }

        if (str.IsWholeStringSinglePlaceholder())
        {
            return str.ReplacePlaceholders(asPlaceHolder, inputs, stringComparison);
        }

        var result = new StringBuilder(str.Length + 16);
        Dictionary<string, string?>? resolvedValuesByTag = null;
        bool insideString = false;
        int i = 0;

        while (i < str.Length)
        {
            if (TryMatchInputPlaceholder(str, i, asPlaceHolder, inputs, stringComparison,
                    out int length, out string tag, out string? value))
            {
                resolvedValuesByTag ??= new(StringComparer.FromComparison(stringComparison));

                if (!resolvedValuesByTag.TryGetValue(tag, out string? resolvedValue))
                {
                    resolvedValue = ResolveNestedInputPlaceholders(value, asPlaceHolder, inputs, stringComparison);
                    resolvedValuesByTag[tag] = resolvedValue;
                }

                result.Append(insideString
                    ? JsonEscapeValue(tag, resolvedValue, onPreEscapedValueSkipped)
                    : resolvedValue ?? "");
                i += length;
                continue;
            }

            char c = str[i];

            if (insideString && c == '\\' && i + 1 < str.Length)
            {
                result.Append(c).Append(str[i + 1]);
                i += 2;
                continue;
            }

            if (c == '"')
            {
                insideString = !insideString;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    private static bool TryMatchInputPlaceholder(
        string str,
        int index,
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison,
        out int length,
        out string tag,
        out string? value)
    {
        length = 0;
        tag = "";
        value = null;

        if (asPlaceHolder)
        {
            if (str[index] != '{')
            {
                return false;
            }

            Match match = ANCHORED_PLACEHOLDER().Match(str, index);
            if (!match.Success)
            {
                return false;
            }

            string candidate = match.Groups["tag"].Value;

            foreach ((string key, string? inputText) in inputs)
            {
                if (string.Equals(key, candidate, stringComparison))
                {
                    length = match.Length;
                    tag = candidate;
                    value = inputText;
                    return true;
                }
            }

            return false;
        }

        foreach ((string key, string? inputText) in inputs)
        {
            if (key.Length > 0
                && index + key.Length <= str.Length
                && string.Compare(str, index, key, 0, key.Length, stringComparison) == 0)
            {
                length = key.Length;
                tag = key;
                value = inputText;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves tags nested inside a substituted value (a text input's value is a template too)
    /// with one plain replacement pass — forward chains resolve in the inputs' insertion order,
    /// the same sequential semantics as <see cref="ReplacePlaceholders"/> — so the value is
    /// composed before it is sanitized. Do not inline away: sanitized substitution is
    /// single-pass and never re-scans substituted output, so this call is where nested input
    /// references resolve.
    /// </summary>
    private static string? ResolveNestedInputPlaceholders(
        string? value,
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison)
    {
        return value.ReplacePlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    private static string JsonEscapeValue(
        string placeholder,
        string? value,
        Action<string>? onPreEscapedValueSkipped)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value.Contains('\\') && $"\"{value}\"".IsValidJson() == true)
        {
            onPreEscapedValueSkipped?.Invoke(placeholder);
            return value;
        }

        var escaped = new StringBuilder(value.Length + 8);

        foreach (char c in value)
        {
            _ = c switch
            {
                '\\' => escaped.Append("\\\\"),
                '"' => escaped.Append("\\\""),
                '\n' => escaped.Append("\\n"),
                '\r' => escaped.Append("\\r"),
                '\t' => escaped.Append("\\t"),
                < ' ' => escaped.Append("\\u").Append(((int)c).ToString("x4")),
                _ => escaped.Append(c),
            };
        }

        return escaped.ToString();
    }

    public static bool IsWholeStringSinglePlaceholder(this string str)
    {
        return WHOLE_STRING_SINGLE_PLACEHOLDER().IsMatch(str.AsSpan().Trim());
    }

    private static string? ReplacePlaceholdersWithTransformedValues(
        this string? str,
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison,
        Func<string, string?, string> transformValue)
    {
        if (str is null)
        {
            return null;
        }

        string? Resolve(string? value) =>
            ResolveNestedInputPlaceholders(value, asPlaceHolder, inputs, stringComparison);

        foreach ((string placeholder, string? inputText) in inputs)
        {
            string? transformed = null;

            if (asPlaceHolder)
            {
                string doubleBraced = $"{{{{{placeholder}}}}}";
                string singleBraced = $"{{{placeholder}}}";

                if (str.Contains(doubleBraced, stringComparison))
                {
                    transformed = transformValue(placeholder, Resolve(inputText));
                    str = str.Replace(doubleBraced, transformed, stringComparison);
                }

                if (str.Contains(singleBraced, stringComparison))
                {
                    transformed ??= transformValue(placeholder, Resolve(inputText));
                    str = str.Replace(singleBraced, transformed, stringComparison);
                }
            }
            else if (str.Contains(placeholder, stringComparison))
            {
                str = str.Replace(placeholder, transformValue(placeholder, Resolve(inputText)), stringComparison);
            }
        }

        return str;
    }

    public static string GetSha256Hash(this string str)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(str);
        byte[] inputHash = SHA256.HashData(inputBytes);
        return Convert.ToHexString(inputHash);
    }

    public static string GetSha256HashLowercase(this string str)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(str)));
    }

    /// <summary>
    /// The name of a copy of <paramref name="name"/>: the name with " (copy)", shortened first so the
    /// result fits <paramref name="maxLength"/>.
    /// </summary>
    public static string AsCopyName(this string name, int maxLength)
    {
        return $"{name.Truncate(maxLength - COPY_SUFFIX.Length).TrimEnd()}{COPY_SUFFIX}";
    }

    /// <summary>
    /// Clips text to a column's <c>[StringLength]</c> so a long value is stored cut rather than
    /// rejected on save. A cut that would split a surrogate pair drops the whole character, so the
    /// result is never invalid UTF-16.
    /// </summary>
    [return: NotNullIfNotNull(nameof(text))]
    public static string? Truncate(this string? text, int maxLength)
    {
        if (text is null || text.Length <= maxLength)
        {
            return text;
        }

        int end = maxLength > 0 && char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return text[..end];
    }

    /// <summary>
    /// The name with the extension appended, unless it already ends with it (case-insensitively);
    /// an empty extension leaves the name alone.
    /// </summary>
    public static string WithExtension(this string name, string extension)
    {
        return extension.Length == 0 || name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? name
            : name + extension;
    }

    /// <summary>
    /// The trimmed text, or null when there is nothing but whitespace — for optional columns
    /// that should never store an empty string.
    /// </summary>
    public static string? NullIfWhiteSpace(this string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>
    /// The text as a lowercase slug: letters and digits kept, every other run collapsed to one
    /// hyphen, no leading or trailing hyphen. Empty when nothing survives.
    /// </summary>
    public static string ToSlug(this string text)
    {
        StringBuilder slug = new(text.Length);
        foreach (char character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                _ = slug.Append(char.ToLowerInvariant(character));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                _ = slug.Append('-');
            }
        }

        return slug.ToString().Trim('-');
    }

    public static bool? IsValidJson(this string str)
    {
        if (string.IsNullOrWhiteSpace(str))
        {
            return false;
        }

        try
        {
            _ = JsonNode.Parse(str);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsInvalidBlockProperty(
        this string? str,
        string? noDataText)
    {
        if (string.IsNullOrWhiteSpace(str))
        {
            return true;
        }

        // the string is a placeholder like this: {Name-of_placeholder[0].Name} or {{Name1.answer}} but is not a valid json
        // Check for both single and double brace placeholders
        string trimmed = str.Trim();
        bool looksLikePlaceholder = (trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
                                    (trimmed.StartsWith("{{") && trimmed.EndsWith("}}"));
        return (looksLikePlaceholder && str.IsValidJson() == false) || (!string.IsNullOrWhiteSpace(noDataText) && str.Trim() == noDataText);
    }

    [GeneratedRegex(@"\p{Lu}\p{Ll}*")]
    private static partial Regex SPLIT_CAMEL_CASE();

    /// <summary>
    /// Returns a compiled regular expression that matches placeholders inside curly braces,
    /// supporting both single braces {placeholder} and double braces {{placeholder}}.
    /// Captures identifiers composed of:
    /// - Letters (a-z, A-Z)
    /// - Digits (0–9)
    /// - Dots (.)
    /// - Underscores (_)
    /// - Dashes (-)
    /// - Square brackets ([, ])
    /// - Spaces ( )
    /// - Ampersands (&)
    /// - Forward slashes (/)
    /// - Parentheses ((), ())
    /// - Percent symbols (%)
    ///
    /// Examples of matched placeholders:
    /// - {Name}
    /// - {{Name}}
    /// - {user_id}
    /// - {{user_id}}
    /// - {items[0].value-part}
    /// - {counterparty[1].Name}
    /// - {First Name}
    /// - {Parent & Child}
    /// - {directory/path}
    /// - {value(option)}
    /// - {percentage%}
    /// </summary>
    [GeneratedRegex(@"\{\{?([a-zA-Z0-9._\-\[\] &/()%]+)\}?\}")]
    private static partial Regex GET_PLACEHOLDERS();

    /// <summary>
    /// Anchored variant of <see cref="GET_PLACEHOLDERS"/>: the entire string is exactly
    /// one placeholder.
    /// </summary>
    [GeneratedRegex(@"^\{\{?[a-zA-Z0-9._\-\[\] &/()%]+\}?\}$")]
    private static partial Regex WHOLE_STRING_SINGLE_PLACEHOLDER();

    /// <summary>
    /// <see cref="GET_PLACEHOLDERS"/> anchored to the scan position (<c>\G</c>) with balanced
    /// braces, so a single placeholder is matched exactly where it begins during a left-to-right
    /// walk. Balancing matters: <c>{tag}}</c> must match only <c>{tag}</c>, leaving the trailing
    /// brace as structure — otherwise a placeholder before a closing <c>}</c> would swallow it.
    /// </summary>
    [GeneratedRegex(@"\G(?:\{\{(?<tag>[a-zA-Z0-9._\-\[\] &/()%]+)\}\}|\{(?<tag>[a-zA-Z0-9._\-\[\] &/()%]+)\})")]
    private static partial Regex ANCHORED_PLACEHOLDER();
}
