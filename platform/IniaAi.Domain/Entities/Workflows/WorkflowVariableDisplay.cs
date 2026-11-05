using IniaAi.Domain.Entities.Workflows.Enums;
using System.Globalization;
using System.Text.Json;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// A run's variable values as people read them: a dropdown or multi-select value as its option
/// labels, a list as its items joined by commas. The run page and the screening judge both read
/// values through it, so the two never disagree on what a run was sent.
/// </summary>
public static class WorkflowVariableDisplay
{
    private const string ITEM_SEPARATOR = ", ";

    /// <summary>Whether a stored value counts as given: not null, not an empty text, not an empty list.</summary>
    public static bool HasValue(JsonElement? element)
    {
        if (element is not { } value)
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => false,
            JsonValueKind.String => !string.IsNullOrEmpty(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() > 0,
            _ => true,
        };
    }

    /// <summary>
    /// The value a run holds for <paramref name="tag"/>. The exact tag wins over a case-insensitive
    /// twin, and twins never throw: executions stored before inbound keys were canonicalized can
    /// carry both casings of one variable, and the stored column camel-cases its keys.
    /// </summary>
    public static JsonElement? FindValue(IReadOnlyDictionary<string, JsonElement>? values, string tag)
    {
        if (values is null)
        {
            return null;
        }

        if (values.TryGetValue(tag, out JsonElement exact))
        {
            return exact;
        }

        foreach ((string key, JsonElement value) in values)
        {
            if (string.Equals(key, tag, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    public static string ValueOf(WorkflowVariableTypes type, IReadOnlyDictionary<string, string> options, JsonElement value)
    {
        return type switch
        {
            WorkflowVariableTypes.Dropdown => LabelOf(value, options),
            WorkflowVariableTypes.MultiSelect => value.ValueKind == JsonValueKind.Array
                ? string.Join(ITEM_SEPARATOR, value.EnumerateArray().Select(item => LabelOf(item, options)))
                : SingleValueOf(value) ?? string.Empty,
            WorkflowVariableTypes.List => value.ValueKind == JsonValueKind.Array
                ? string.Join(ITEM_SEPARATOR, value.EnumerateArray().Select(item => SingleValueOf(item) ?? string.Empty))
                : SingleValueOf(value) ?? string.Empty,
            _ => SingleValueOf(value) ?? string.Empty,
        };
    }

    private static string LabelOf(JsonElement value, IReadOnlyDictionary<string, string> options)
    {
        string key = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
        return options.TryGetValue(key, out string? label) ? label : key;
    }

    private static string? SingleValueOf(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out long asLong)
                ? asLong.ToString(CultureInfo.InvariantCulture)
                : value.TryGetDecimal(out decimal asDecimal)
                    ? asDecimal.ToString(CultureInfo.InvariantCulture)
                    : value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Array => string.Join(ITEM_SEPARATOR, value.EnumerateArray().Select(item => SingleValueOf(item) ?? string.Empty)),
            _ => value.ToString(),
        };
    }
}
