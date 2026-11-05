using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IniaAi.Common.Reflection;

/// <summary>
/// Reflection-based helper for placeholder operations on any object.
/// Automatically discovers string, Dictionary&lt;string, string&gt;, and List&lt;string&gt; properties
/// and performs placeholder replacement, detection, and extraction across all of them.
/// </summary>
public static class PlaceholderReflectionHelper
{
    private static readonly ConcurrentDictionary<Type, SanitizedProperty[]> _stringPropsCache = new();
    private static readonly ConcurrentDictionary<Type, SanitizedProperty[]> _dictPropsCache = new();
    private static readonly ConcurrentDictionary<Type, SanitizedProperty[]> _listPropsCache = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _authoredStringPropsCache = new();

    private readonly record struct SanitizedProperty(PropertyInfo Property, PlaceholderSanitization Sanitization);

    /// <summary>
    /// Replace placeholders in all string, dictionary-value, and list-item properties,
    /// applying each property's declared <see cref="PlaceholderSanitization"/> to the
    /// substituted values.
    /// </summary>
    public static void ReplaceAll(
        object target,
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison)
    {
        Type type = target.GetType();
        ISanitizationWarningSink? sink = target as ISanitizationWarningSink;

        foreach ((PropertyInfo prop, PlaceholderSanitization sanitization) in GetStringProperties(type))
        {
            string? value = (string?)prop.GetValue(target);

            string? replaced = ReplaceWithSanitization(value, sanitization, asPlaceHolder, inputs, stringComparison, sink, prop.Name);

            prop.SetValue(target, replaced);
        }

        foreach ((PropertyInfo prop, PlaceholderSanitization sanitization) in GetDictProperties(type))
        {
            if (prop.GetValue(target) is not IDictionary<string, string> dict)
            {
                continue;
            }

            foreach (string key in dict.Keys.ToList())
            {
                dict[key] = ReplaceWithSanitization(dict[key], sanitization, asPlaceHolder, inputs, stringComparison, sink, prop.Name, headerName: key)
                    ?? dict[key];
            }
        }

        foreach ((PropertyInfo prop, PlaceholderSanitization sanitization) in GetListProperties(type))
        {
            if (prop.GetValue(target) is not List<string> list)
            {
                continue;
            }

            for (int i = 0; i < list.Count; i++)
            {
                list[i] = ReplaceWithSanitization(list[i], sanitization, asPlaceHolder, inputs, stringComparison, sink, prop.Name)
                    ?? list[i];
            }
        }
    }

    /// <summary>
    /// Check if any string, dictionary-value, or list-item property contains the placeholder.
    /// </summary>
    public static bool HasPlaceholder(
        object target,
        string placeholder,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        Type type = target.GetType();

        foreach ((PropertyInfo prop, _) in GetStringProperties(type))
        {
            string? value = (string?)prop.GetValue(target);
            if (value?.Contains(placeholder, stringComparison) == true)
            {
                return true;
            }
        }

        foreach ((PropertyInfo prop, _) in GetDictProperties(type))
        {
            if (prop.GetValue(target) is not IDictionary<string, string> dict)
            {
                continue;
            }

            foreach (string val in dict.Values)
            {
                if (val.Contains(placeholder, stringComparison))
                {
                    return true;
                }
            }
        }

        foreach ((PropertyInfo prop, _) in GetListProperties(type))
        {
            if (prop.GetValue(target) is not List<string> list)
            {
                continue;
            }

            foreach (string item in list)
            {
                if (!item.Contains(placeholder, stringComparison))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extract all placeholders from all string, dictionary-value, and list-item properties.
    /// </summary>
    public static IEnumerable<string> GetAllPlaceholders(object target)
    {
        Type type = target.GetType();
        HashSet<string> seen = [];

        foreach ((PropertyInfo prop, _) in GetStringProperties(type))
        {
            string? value = (string?)prop.GetValue(target);

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (string placeholder in value.GetPlaceholders())
            {
                if (!seen.Add(placeholder))
                {
                    continue;
                }

                yield return placeholder;
            }
        }

        foreach ((PropertyInfo prop, _) in GetDictProperties(type))
        {
            if (prop.GetValue(target) is not IDictionary<string, string> dict)
            {
                continue;
            }

            foreach (string val in dict.Values)
            {
                if (string.IsNullOrWhiteSpace(val))
                {
                    continue;
                }

                foreach (string placeholder in val.GetPlaceholders())
                {
                    if (!seen.Add(placeholder))
                    {
                        continue;
                    }

                    yield return placeholder;
                }
            }
        }

        foreach ((PropertyInfo prop, _) in GetListProperties(type))
        {
            if (prop.GetValue(target) is not List<string> list)
            {
                continue;
            }

            foreach (string item in list)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                foreach (string placeholder in item.GetPlaceholders())
                {
                    if (!seen.Add(placeholder))
                    {
                        continue;
                    }

                    yield return placeholder;
                }
            }
        }
    }

    /// <summary>
    /// Returns the raw template strings (string, dictionary-value, and list-item properties)
    /// discovered the same way as <see cref="GetAllPlaceholders"/>, without extracting tags —
    /// for callers that need the original text (e.g. validating nested parameterized references).
    /// </summary>
    public static IEnumerable<string> GetAllTemplateStrings(object target)
    {
        return GetTemplateSlots(target).Select(static slot => slot.Value);
    }

    /// <summary>
    /// Rewrites in place every template string <see cref="GetAllTemplateStrings"/> discovers, so a
    /// caller that must edit authored templates covers exactly the fields the engine reads. A value
    /// the transform leaves unchanged is not written back, so an identity transform never writes.
    /// </summary>
    public static void TransformAllTemplateStrings(object target, Func<string, string> transform)
    {
        foreach (TemplateSlot slot in GetTemplateSlots(target).ToList())
        {
            string transformed = transform(slot.Value);

            if (!string.Equals(transformed, slot.Value, StringComparison.Ordinal))
            {
                slot.Write(transformed);
            }
        }
    }

    private readonly record struct TemplateSlot(string Value, Action<string> Write);

    private static IEnumerable<TemplateSlot> GetTemplateSlots(object target)
    {
        Type type = target.GetType();

        foreach (PropertyInfo prop in GetAuthoredStringProperties(type))
        {
            if ((string?)prop.GetValue(target) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                yield return new TemplateSlot(value, text => prop.SetValue(target, text));
            }
        }

        foreach ((PropertyInfo prop, _) in GetDictProperties(type))
        {
            if (prop.GetValue(target) is not IDictionary<string, string> dict)
            {
                continue;
            }

            foreach (string key in dict.Keys.ToList())
            {
                if (!string.IsNullOrWhiteSpace(dict[key]))
                {
                    yield return new TemplateSlot(dict[key], text => dict[key] = text);
                }
            }
        }

        foreach ((PropertyInfo prop, _) in GetListProperties(type))
        {
            if (prop.GetValue(target) is not List<string> list)
            {
                continue;
            }

            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                if (!string.IsNullOrWhiteSpace(list[index]))
                {
                    yield return new TemplateSlot(list[index], text => list[index] = text);
                }
            }
        }
    }

    // Execution-output strings (e.g. AI-generated previews) are not authored templates.
    private static PropertyInfo[] GetAuthoredStringProperties(Type type)
    {
        return _authoredStringPropsCache.GetOrAdd(type, static t =>
            [.. GetStringProperties(t)
                .Select(static sanitized => sanitized.Property)
                .Where(static prop => !prop.IsDefined(typeof(NotAReplacementTemplateAttribute), inherit: true))]);
    }

    private static string? ReplaceWithSanitization(
        string? value,
        PlaceholderSanitization sanitization,
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison,
        ISanitizationWarningSink? sink,
        string propertyName,
        string? headerName = null)
    {
        string? replaced = sanitization switch
        {
            PlaceholderSanitization.Url =>
                value.ReplacePlaceholdersWithUrlEncodedValues(asPlaceHolder, inputs, stringComparison),
            PlaceholderSanitization.Json =>
                value.ReplacePlaceholdersWithJsonEscapedValues(
                    asPlaceHolder,
                    inputs,
                    stringComparison,
                    sink is null
                        ? null
                        : tag => sink.AddSanitizationWarning(
                            $"Value substituted for placeholder '{tag}' into '{propertyName}' was already JSON-escaped; engine escaping skipped.")),
            PlaceholderSanitization.HeaderValue =>
                value.ReplacePlaceholders(asPlaceHolder, WithAsciiJsonContainers(value, inputs, stringComparison), stringComparison),
            _ => value.ReplacePlaceholders(asPlaceHolder, inputs, stringComparison),
        };

        if (sanitization == PlaceholderSanitization.HeaderValue)
        {
            ThrowIfInvalidHeaderValue(propertyName, replaced, headerName);
        }

        return replaced;
    }

    /// <summary>
    /// HTTP headers carry ASCII only, and the client refuses to send anything else. A placeholder
    /// that selects a JSON object or array renders letters of every script as written, so in a
    /// header such a value is rewritten with ASCII escapes, the form it always had there. Every
    /// other value substitutes as it is.
    /// </summary>
    private static Dictionary<string, string?> WithAsciiJsonContainers(
        string? template,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison)
    {
        if (string.IsNullOrEmpty(template))
        {
            return inputs;
        }

        Dictionary<string, string?>? rewritten = null;
        foreach ((string key, string? value) in inputs)
        {
            if (template.Contains(key, stringComparison) && AsciiJsonContainer(value) is string ascii)
            {
                rewritten ??= new Dictionary<string, string?>(inputs, inputs.Comparer);
                rewritten[key] = ascii;
            }
        }

        return rewritten ?? inputs;
    }

    private static string? AsciiJsonContainer(string? value)
    {
        if (string.IsNullOrEmpty(value) || Ascii.IsValid(value))
        {
            return null;
        }

        ReadOnlySpan<char> trimmed = value.AsSpan().Trim();
        if (trimmed.IsEmpty || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(value) is JsonNode container and (JsonObject or JsonArray)
                ? container.ToJsonString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ThrowIfInvalidHeaderValue(string propertyName, string? value, string? headerName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (char c in value)
        {
            if (char.IsControl(c) && c != '\t')
            {
                string target = headerName is null
                    ? $"'{propertyName}'"
                    : $"'{propertyName}' (header '{headerName}')";
                throw new InvalidOperationException(
                    $"Value resolved for {target} contains control characters (e.g. CR/LF) that are not valid in an HTTP header value.");
            }
        }
    }

    private static PlaceholderSanitization GetSanitization(PropertyInfo property)
    {
        return property.GetCustomAttribute<SanitizePlaceholderValuesAttribute>(inherit: true)?.Mode
            ?? PlaceholderSanitization.None;
    }

    private static SanitizedProperty[] ToSanitizedProperties(IEnumerable<PropertyInfo> properties)
    {
        return [.. properties
            .Where(p => !p.IsDefined(typeof(PlaceholderReplacementIgnoreAttribute), inherit: true))
            .Select(p => new SanitizedProperty(p, GetSanitization(p)))];
    }

    private static SanitizedProperty[] GetStringProperties(Type type)
    {
        return _stringPropsCache.GetOrAdd(type, static t =>
            ToSanitizedProperties(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string) && p.CanRead && p.CanWrite)));
    }

    private static SanitizedProperty[] GetDictProperties(Type type)
    {
        return _dictPropsCache.GetOrAdd(type, static t =>
            ToSanitizedProperties(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && typeof(IDictionary<string, string>).IsAssignableFrom(p.PropertyType))));
    }

    private static SanitizedProperty[] GetListProperties(Type type)
    {
        return _listPropsCache.GetOrAdd(type, static t =>
            ToSanitizedProperties(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.PropertyType == typeof(List<string>))));
    }
}
