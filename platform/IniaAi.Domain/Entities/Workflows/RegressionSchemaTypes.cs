using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// The value kinds a workflow-level regression field definition can declare.
/// </summary>
public static class RegressionFieldTypes
{
    public const string NUMERIC = "numeric";
    public const string TEXT = "text";

    public static readonly IReadOnlyList<string> ALL = [NUMERIC, TEXT];

    public static bool IsKnown(string? type)
    {
        return type is not null
            && ALL.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsNumeric(string? type)
    {
        return string.Equals(type, NUMERIC, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// How a regression schema field's value is located in a block's output. When a field declares a
/// source, extraction is deterministic (no auto-detection) and a miss fails loudly; when it is null
/// the legacy tag -> json -> llm fallback chain is used.
/// </summary>
public static class RegressionSchemaSources
{
    public const string XML = "xml";
    public const string JSON = "json";
    public const string REGEX = "regex";
    public const string LLM = "llm";

    public static readonly IReadOnlyList<string> ALL = [XML, JSON, REGEX, LLM];

    public static bool IsKnown(string? source)
    {
        return source is not null
            && ALL.Any(s => string.Equals(s, source, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsLlm(string? source)
    {
        return string.Equals(source, LLM, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsXml(string? source)
    {
        return string.Equals(source, XML, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRegex(string? source)
    {
        return string.Equals(source, REGEX, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Defines a single field in a regression schema with its type, comparison tolerance, and (optionally)
/// how its value is located in the block output.
/// </summary>
/// <param name="Type">One of: "numeric", "text"</param>
/// <param name="Tolerance">Percentage tolerance for numeric comparisons (e.g., 5 for ±5%, 0.5 for ±0.5%).
/// Note: when the baseline value is 0, any non-zero rerun value is always classified as ValueDiffers
/// regardless of tolerance (division by zero in percentage diff calculation).</param>
/// <param name="Source">Where the value comes from: one of <see cref="RegressionSchemaSources"/>
/// (xml/json/regex/llm), or null to use the legacy tag -> json -> llm fallback.</param>
/// <param name="Locator">The xml tag name, json path, or regex pattern (capture group 1 is the value).
/// For xml/json, defaults to the field name when omitted.</param>
public record RegressionSchemaField(
    string Type,
    decimal Tolerance,
    string? Source = null,
    string? Locator = null);

/// <summary>
/// A fact extracted from a block response by the LLM, containing both display and numeric representations.
/// </summary>
/// <param name="Display">Human-readable display string (e.g., "$18.8B", "3.5%")</param>
/// <param name="Numeric">Parsed numeric value for tolerance comparison, or null if not extractable</param>
public record ExtractedFact(string? Display, decimal? Numeric);

/// <summary>
/// A block's half of a regression field: how the value named by a workflow-level
/// <see cref="Entities.Workflows.WorkflowRegressionField"/> is located in this block's output.
/// Type and tolerance live on the workflow definition, not here.
/// </summary>
/// <param name="Source">One of <see cref="RegressionSchemaSources"/>, or null for the legacy
/// tag -> json -> llm fallback.</param>
/// <param name="Locator">The xml tag name, json path, or regex pattern (capture group 1 is the value).
/// For xml/json, defaults to the field name when omitted.</param>
public record RegressionFieldBinding(
    string? Source = null,
    string? Locator = null);

/// <summary>
/// The single (de)serializer for <see cref="Entities.Workflows.Blocks.Block.RegressionSchema"/> binding
/// JSON (shape: {"fieldName": {"Source": "xml", "Locator": "aum"}}). Every consumer (editor, validator,
/// comparison, legacy hoist) must go through this class so casing and shape never drift. Reads are
/// case-insensitive and silently ignore unknown members, so legacy full-shape JSON (with Type/Tolerance
/// on the block) parses as bindings without error.
/// </summary>
public static class RegressionSchemaBindings
{
    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Parses binding JSON into a case-insensitive dictionary keyed by field name. Keys differing
    /// only by case collapse to the first occurrence (matching the migration's and hoist's
    /// first-binder-wins rule) instead of throwing. Returns an empty dictionary for null/blank input;
    /// throws <see cref="JsonException"/> on malformed JSON (callers that must not crash on
    /// hand-edited data should use <see cref="TryParse"/>).
    /// </summary>
    public static Dictionary<string, RegressionFieldBinding> Parse(string? bindingJson)
    {
        var result = new Dictionary<string, RegressionFieldBinding>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(bindingJson))
        {
            return result;
        }

        Dictionary<string, RegressionFieldBinding>? parsed = JsonSerializer
            .Deserialize<Dictionary<string, RegressionFieldBinding>>(bindingJson, _readOptions);
        foreach ((string fieldName, RegressionFieldBinding binding) in parsed ?? [])
        {
            _ = result.TryAdd(fieldName, binding);
        }

        return result;
    }

    /// <summary>Non-throwing variant of <see cref="Parse"/>; false when the JSON is malformed.</summary>
    public static bool TryParse(string? bindingJson, out Dictionary<string, RegressionFieldBinding> bindings)
    {
        try
        {
            bindings = Parse(bindingJson);
            return true;
        }
        catch (JsonException)
        {
            bindings = new Dictionary<string, RegressionFieldBinding>(StringComparer.OrdinalIgnoreCase);
            return false;
        }
    }

    /// <summary>Serializes bindings back to the on-disk shape; null when there are no entries.</summary>
    public static string? Serialize(IReadOnlyDictionary<string, RegressionFieldBinding> bindings)
    {
        if (bindings.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(bindings, _writeOptions);
    }

    /// <summary>
    /// True when the JSON still carries the legacy full-shape entries (Type/Tolerance stored on the
    /// block) that predate workflow-level definitions and need hoisting.
    /// </summary>
    public static bool AnyLegacyKeys(string? bindingJson)
    {
        if (string.IsNullOrWhiteSpace(bindingJson))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bindingJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (JsonProperty field in document.RootElement.EnumerateObject())
            {
                if (field.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonProperty member in field.Value.EnumerateObject())
                {
                    if (member.Name.Equals(nameof(RegressionSchemaField.Type), StringComparison.OrdinalIgnoreCase)
                        || member.Name.Equals(nameof(RegressionSchemaField.Tolerance), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }
}
