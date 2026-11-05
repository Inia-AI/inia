using System.Text.Json;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// The effective variable values of a submission, canonicalized against a template's variable
/// definitions so that two submissions of the same inputs compare equal regardless of key casing,
/// omitted-vs-explicit defaults, a number arriving as a JSON string, a list arriving as a
/// <c>|</c>-separated string, or a date arriving with a time of day. Stored execution variables
/// round-trip through a camelCase JSON column, so this is the only fair way to ask "were these two
/// runs given the same inputs?".
/// Deliberately not the job-input converters (<c>ConvertVariablesToStrings</c>): those format
/// values for the engine — host-only URLs, culture-dependent numbers — and would make equal
/// inputs fingerprint unequal.
/// </summary>
public sealed class WorkflowVariableFingerprint
{
    private WorkflowVariableFingerprint(IReadOnlyDictionary<string, string> canonicalValues)
    {
        CanonicalValues = canonicalValues;
    }

    public IReadOnlyDictionary<string, string> CanonicalValues { get; }

    public static WorkflowVariableFingerprint Compute(
        IEnumerable<WorkflowVariable> definitions,
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        var canonical = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (WorkflowVariable definition in definitions)
        {
            if (string.IsNullOrEmpty(definition.ReplacementTag))
            {
                continue;
            }

            JsonElement? element = WorkflowVariableDisplay.FindValue(values, definition.ReplacementTag) ?? definition.DefaultValueAsJson();
            string? value = element is null ? null : Canonicalize(definition, element.Value);

            if (value is not null)
            {
                canonical[definition.ReplacementTag] = value;
            }
        }

        return new WorkflowVariableFingerprint(canonical);
    }

    /// <summary>
    /// The replacement tags whose canonical value differs between this fingerprint and
    /// <paramref name="other"/>, in ordinal order.
    /// </summary>
    public IReadOnlyList<string> DifferencesFrom(WorkflowVariableFingerprint other)
    {
        return [.. CanonicalValues.Keys
            .Union(other.CanonicalValues.Keys, StringComparer.Ordinal)
            .Where(tag => !string.Equals(
                CanonicalValues.GetValueOrDefault(tag),
                other.CanonicalValues.GetValueOrDefault(tag),
                StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
    }

    private static string? Canonicalize(WorkflowVariable definition, JsonElement element)
    {
        return Canonicalize(definition.Type, element) is { } text ? definition.ToPlaceholderText(text) : null;
    }

    private static string? Canonicalize(WorkflowVariableTypes type, JsonElement element)
    {
        if (type is WorkflowVariableTypes.List or WorkflowVariableTypes.MultiSelect
            && element.ValueKind == JsonValueKind.String)
        {
            return JsonSerializer.Serialize(WorkflowVariable.SplitListItems(element.GetString() ?? string.Empty));
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => JsonSerializer.Serialize(
                element.EnumerateArray().Select(item => Canonicalize(WorkflowVariableTypes.String, item)?.Trim()).ToList()),
            JsonValueKind.Object => element.GetRawText(),
            _ => null,
        };
    }
}
