using IniaAi.Common.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Validates an agent output schema as the JSON Schema dialect the runtime's SDK actually
/// enforces, so a schema the SDK will refuse fails at save instead of after a paid run.
/// The SDK (Agent SDK 0.3.227 CLI) compiles <c>outputFormat.schema</c> with Ajv v8's
/// <b>draft-07</b> class in strict mode with format validation off
/// (<c>new Ajv({allErrors: true, validateFormats: false})</c>, verified against the bundled CLI
/// binary and reproduced against Ajv 8.20): unknown keywords — including draft-2020-12-only
/// ones like <c>prefixItems</c> — are compile errors, keyword shapes are held to the draft-07
/// meta-schema, a non-draft-07 <c>$schema</c> cannot resolve, and <c>format</c> values are
/// accepted unvalidated. Every rule here mirrors a measured Ajv behaviour; deliberate
/// divergences, both marginally stricter: dead <c>$defs</c> entries are validated even though
/// Ajv only compiles referenced ones, and regex validity is checked with .NET regexes rather
/// than JavaScript's stricter <c>u</c>-flag grammar.
/// </summary>
public static class AgentOutputSchemaDraft07
{
    public sealed record SchemaViolation(string Path, string Message);

    private const int MAX_DEPTH = 512;
    private const string ROOT_PATH = "#";

    private static readonly HashSet<string> _anyValueKeywords = new(StringComparer.Ordinal)
    {
        "const", "default", "examples", "deprecated", "writeOnly", "contentSchema", "$vocabulary", "$async",
    };

    private static readonly HashSet<string> _stringKeywords = new(StringComparer.Ordinal)
    {
        "title", "description", "$comment", "format", "contentMediaType", "contentEncoding", "$id",
    };

    private static readonly HashSet<string> _booleanKeywords = new(StringComparer.Ordinal)
    {
        "uniqueItems", "readOnly", "nullable",
    };

    private static readonly HashSet<string> _numberKeywords = new(StringComparer.Ordinal)
    {
        "maximum", "minimum", "exclusiveMaximum", "exclusiveMinimum",
    };

    private static readonly HashSet<string> _nonNegativeIntegerKeywords = new(StringComparer.Ordinal)
    {
        "maxLength", "minLength", "maxItems", "minItems", "maxProperties", "minProperties",
    };

    private static readonly HashSet<string> _subschemaKeywords = new(StringComparer.Ordinal)
    {
        "additionalItems", "additionalProperties", "propertyNames", "contains", "not", "if", "then", "else",
    };

    private static readonly HashSet<string> _schemaMapKeywords = new(StringComparer.Ordinal)
    {
        "properties", "patternProperties", "definitions", "$defs",
    };

    private static readonly HashSet<string> _schemaArrayKeywords = new(StringComparer.Ordinal)
    {
        "allOf", "anyOf", "oneOf",
    };

    private static readonly HashSet<string> _simpleTypes = new(StringComparer.Ordinal)
    {
        "array", "boolean", "integer", "null", "number", "object", "string",
    };

    private static readonly HashSet<string> _draft07SchemaIds = new(StringComparer.Ordinal)
    {
        "http://json-schema.org/draft-07/schema",
        "http://json-schema.org/schema",
    };

    public static List<SchemaViolation> Validate(JsonObject schema)
    {
        var violations = new List<SchemaViolation>();
        ValidateSubschema(schema, ROOT_PATH, schema, violations, depth: 0);
        return violations;
    }

    private static void ValidateSubschema(
        JsonNode? node,
        string path,
        JsonObject root,
        List<SchemaViolation> violations,
        int depth)
    {
        if (depth > MAX_DEPTH)
        {
            violations.Add(new SchemaViolation(path, $"schema nests deeper than {MAX_DEPTH} levels"));
            return;
        }

        if (node is JsonValue value && value.TryGetValue(out bool _))
        {
            return;
        }

        if (node is not JsonObject schema)
        {
            violations.Add(new SchemaViolation(path, "must be a schema (an object or a boolean)"));
            return;
        }

        foreach ((string keyword, JsonNode? keywordValue) in schema)
        {
            ValidateKeyword(keyword, keywordValue, path, root, violations, depth);
        }
    }

    private static void ValidateKeyword(
        string keyword,
        JsonNode? value,
        string schemaPath,
        JsonObject root,
        List<SchemaViolation> violations,
        int depth)
    {
        string path = JsonPointer.Append(schemaPath, keyword);

        if (_anyValueKeywords.Contains(keyword))
        {
            return;
        }

        if (_stringKeywords.Contains(keyword))
        {
            RequireString(value, path, violations);
            return;
        }

        if (_booleanKeywords.Contains(keyword))
        {
            if (value is not JsonValue booleanValue || !booleanValue.TryGetValue(out bool _))
            {
                violations.Add(new SchemaViolation(path, "must be boolean"));
            }

            return;
        }

        if (_numberKeywords.Contains(keyword))
        {
            if (AsNumber(value) is null)
            {
                violations.Add(new SchemaViolation(path, "must be number"));
            }

            return;
        }

        if (_nonNegativeIntegerKeywords.Contains(keyword))
        {
            if (AsNumber(value) is not decimal integer || decimal.Truncate(integer) != integer || integer < 0)
            {
                violations.Add(new SchemaViolation(path, "must be a non-negative integer"));
            }

            return;
        }

        if (_subschemaKeywords.Contains(keyword))
        {
            ValidateSubschema(value, path, root, violations, depth + 1);
            return;
        }

        if (_schemaMapKeywords.Contains(keyword))
        {
            ValidateSchemaMap(keyword, value, path, root, violations, depth);
            return;
        }

        if (_schemaArrayKeywords.Contains(keyword))
        {
            ValidateSchemaArray(value, path, root, violations, depth);
            return;
        }

        switch (keyword)
        {
            case "$schema":
                ValidateDollarSchema(value, path, violations);
                return;
            case "$ref":
                ValidateRef(value, path, root, violations);
                return;
            case "type":
                ValidateType(value, path, violations);
                return;
            case "enum":
                ValidateEnum(value, path, violations);
                return;
            case "required":
                ValidateStringArray(value, path, violations);
                return;
            case "multipleOf":
                if (AsNumber(value) is not > 0)
                {
                    violations.Add(new SchemaViolation(path, "must be a number > 0"));
                }

                return;
            case "pattern":
                ValidateRegexString(value, path, violations);
                return;
            case "items":
                ValidateItems(value, path, root, violations, depth);
                return;
            case "dependencies":
                ValidateDependencies(value, path, root, violations, depth);
                return;
            case "id":
                violations.Add(new SchemaViolation(path, "keyword \"id\" is not supported — use \"$id\""));
                return;
            default:
                violations.Add(new SchemaViolation(
                    schemaPath,
                    $"unknown keyword \"{keyword}\" — the runtime's SDK enforces JSON Schema draft-07 and rejects keywords outside it"));
                return;
        }
    }

    private static void ValidateSchemaMap(
        string keyword,
        JsonNode? value,
        string path,
        JsonObject root,
        List<SchemaViolation> violations,
        int depth)
    {
        if (value is not JsonObject map)
        {
            violations.Add(new SchemaViolation(path, "must be an object of schemas"));
            return;
        }

        foreach ((string key, JsonNode? subschema) in map)
        {
            if (keyword == "patternProperties" && !CompilesAsRegex(key))
            {
                violations.Add(new SchemaViolation(path, $"property pattern \"{key}\" is not a valid regular expression"));
            }

            ValidateSubschema(subschema, JsonPointer.Append(path, key), root, violations, depth + 1);
        }
    }

    private static void ValidateSchemaArray(
        JsonNode? value,
        string path,
        JsonObject root,
        List<SchemaViolation> violations,
        int depth)
    {
        if (value is not JsonArray array || array.Count == 0)
        {
            violations.Add(new SchemaViolation(path, "must be a non-empty array of schemas"));
            return;
        }

        for (int index = 0; index < array.Count; index++)
        {
            ValidateSubschema(array[index], $"{path}/{index}", root, violations, depth + 1);
        }
    }

    private static void ValidateItems(
        JsonNode? value,
        string path,
        JsonObject root,
        List<SchemaViolation> violations,
        int depth)
    {
        if (value is JsonArray)
        {
            ValidateSchemaArray(value, path, root, violations, depth);
            return;
        }

        ValidateSubschema(value, path, root, violations, depth + 1);
    }

    private static void ValidateDependencies(
        JsonNode? value,
        string path,
        JsonObject root,
        List<SchemaViolation> violations,
        int depth)
    {
        if (value is not JsonObject dependencies)
        {
            violations.Add(new SchemaViolation(path, "must be an object"));
            return;
        }

        foreach ((string property, JsonNode? dependency) in dependencies)
        {
            string dependencyPath = JsonPointer.Append(path, property);
            if (dependency is JsonArray)
            {
                ValidateStringArray(dependency, dependencyPath, violations);
            }
            else
            {
                ValidateSubschema(dependency, dependencyPath, root, violations, depth + 1);
            }
        }
    }

    private static void ValidateDollarSchema(JsonNode? value, string path, List<SchemaViolation> violations)
    {
        if (RequireString(value, path, violations) is not string declared)
        {
            return;
        }

        if (!_draft07SchemaIds.Contains(declared.TrimEnd('#')))
        {
            violations.Add(new SchemaViolation(
                path,
                "declares a dialect the runtime's SDK cannot resolve — it enforces JSON Schema draft-07 "
                + "(\"http://json-schema.org/draft-07/schema#\"); omit $schema or declare draft-07"));
        }
    }

    private static void ValidateRef(JsonNode? value, string path, JsonObject root, List<SchemaViolation> violations)
    {
        if (RequireString(value, path, violations) is not string reference)
        {
            return;
        }

        if (!reference.StartsWith('#'))
        {
            violations.Add(new SchemaViolation(
                path,
                $"cannot resolve reference \"{reference}\" — the runtime's SDK loads no external schemas; use a local \"#/...\" pointer"));
            return;
        }

        if (reference.Length > 1 && reference[1] == '/' && !LocalPointerResolves(root, reference))
        {
            violations.Add(new SchemaViolation(path, $"cannot resolve reference \"{reference}\""));
        }
    }

    private static void ValidateType(JsonNode? value, string path, List<SchemaViolation> violations)
    {
        const string EXPECTATION =
            "must be one of array, boolean, integer, null, number, object, string — or a non-empty unique array of those";

        if (value is JsonArray types)
        {
            bool allSimple = types.All(static entry =>
                entry is JsonValue typeName
                && typeName.TryGetValue(out string? name)
                && name is not null
                && _simpleTypes.Contains(name));
            bool unique = types.Select(static entry => entry?.ToJsonString()).Distinct(StringComparer.Ordinal).Count()
                == types.Count;

            if (types.Count == 0 || !allSimple || !unique)
            {
                violations.Add(new SchemaViolation(path, EXPECTATION));
            }

            return;
        }

        if (value is not JsonValue single
            || !single.TryGetValue(out string? singleName)
            || singleName is null
            || !_simpleTypes.Contains(singleName))
        {
            violations.Add(new SchemaViolation(path, EXPECTATION));
        }
    }

    private static void ValidateEnum(JsonNode? value, string path, List<SchemaViolation> violations)
    {
        if (value is not JsonArray entries || entries.Count == 0)
        {
            violations.Add(new SchemaViolation(path, "must be a non-empty array"));
            return;
        }

        for (int first = 0; first < entries.Count; first++)
        {
            for (int second = first + 1; second < entries.Count; second++)
            {
                if (JsonNode.DeepEquals(entries[first], entries[second]))
                {
                    violations.Add(new SchemaViolation(path, "must not contain duplicate values"));
                    return;
                }
            }
        }
    }

    private static void ValidateStringArray(JsonNode? value, string path, List<SchemaViolation> violations)
    {
        if (value is not JsonArray entries)
        {
            violations.Add(new SchemaViolation(path, "must be an array of strings"));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? entry in entries)
        {
            if (entry is not JsonValue entryValue || !entryValue.TryGetValue(out string? text) || text is null)
            {
                violations.Add(new SchemaViolation(path, "must be an array of strings"));
                return;
            }

            if (!seen.Add(text))
            {
                violations.Add(new SchemaViolation(path, "must not contain duplicate entries"));
                return;
            }
        }
    }

    private static void ValidateRegexString(JsonNode? value, string path, List<SchemaViolation> violations)
    {
        if (RequireString(value, path, violations) is not string pattern)
        {
            return;
        }

        if (!CompilesAsRegex(pattern))
        {
            violations.Add(new SchemaViolation(path, "is not a valid regular expression"));
        }
    }

    private static string? RequireString(JsonNode? value, string path, List<SchemaViolation> violations)
    {
        if (value is JsonValue stringValue && stringValue.TryGetValue(out string? text) && text is not null)
        {
            return text;
        }

        violations.Add(new SchemaViolation(path, "must be string"));
        return null;
    }

    private static bool CompilesAsRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static decimal? AsNumber(JsonNode? value)
    {
        if (value is not JsonValue number || number.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        // TryGetValue converts only from a node's backing type: parsed schemas are
        // JsonElement-backed and read as decimal directly, but a schema BUILT in memory (the
        // verification envelope) backs each number with the CLR type it was created from.
        if (number.TryGetValue(out decimal parsed))
        {
            return parsed;
        }

        if (number.TryGetValue(out int intValue))
        {
            return intValue;
        }

        if (number.TryGetValue(out long longValue))
        {
            return longValue;
        }

        if (!number.TryGetValue(out double doubleValue) || !double.IsFinite(doubleValue))
        {
            return null;
        }

        // (double)decimal.MaxValue rounds UP past the decimal range, so a bounds comparison
        // admits values the cast then overflows on — the catch is the only exact guard.
        try
        {
            return (decimal)doubleValue;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool LocalPointerResolves(JsonObject root, string reference)
    {
        JsonNode? current = root;
        foreach (string rawSegment in reference[2..].Split('/'))
        {
            string segment = rawSegment.Replace("~1", "/").Replace("~0", "~");
            switch (current)
            {
                case JsonObject obj when obj.TryGetPropertyValue(segment, out JsonNode? next):
                    current = next;
                    break;
                case JsonArray array when int.TryParse(segment, out int index) && index >= 0 && index < array.Count:
                    current = array[index];
                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
