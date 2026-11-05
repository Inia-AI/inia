using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.OutputFormat;

/// <summary>
/// Two-way translator between a list of <see cref="SchemaFieldDraft"/> rows
/// (the Simple Schema Builder UI model) and a flat JSON Schema string.
/// Round-trips losslessly only for flat objects with primitive properties,
/// optional enum / range / list-item-type. Anything richer (nested objects,
/// oneOf, regex patterns, etc.) is reported via <see cref="ParseResult.IsFullyRepresentable"/>
/// so the UI can route the user to the Advanced editor.
/// </summary>
public static class SimpleSchemaTranslator
{
    public record ParseResult(IReadOnlyList<SchemaFieldDraft> Fields, bool IsFullyRepresentable);

    public static string? ToJsonSchema(IReadOnlyList<SchemaFieldDraft> fields)
    {
        SchemaFieldDraft[] usable = [.. fields.Where(f => !string.IsNullOrWhiteSpace(f.Name))];
        if (usable.Length == 0)
        {
            return null;
        }

        JObject properties = [];
        JArray required = [];

        // Dedup by Name. Last entry's schema wins, required if any group entry was required.
        foreach (IGrouping<string, SchemaFieldDraft> group in usable.GroupBy(f => f.Name, StringComparer.Ordinal))
        {
            SchemaFieldDraft last = group.Last();
            properties[group.Key] = BuildPropertySchema(last);
            if (group.Any(f => f.Required))
            {
                required.Add(group.Key);
            }
        }

        JObject schema = new()
        {
            ["type"] = "object",
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema.ToString(Formatting.Indented);
    }

    public static ParseResult FromJsonSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParseResult([], true);
        }

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch
        {
            return new ParseResult([], false);
        }

        // Only flat objects are representable in the simple builder. If the root isn't
        // an object schema, or has any unsupported keyword, fall back to Advanced.
        if ((string?)root["type"] != "object")
        {
            return new ParseResult([], false);
        }

        if (HasUnsupportedKeyword(root))
        {
            return new ParseResult([], false);
        }

        if (root["properties"] is not JObject properties)
        {
            return new ParseResult([], true);
        }

        HashSet<string> required = [];
        bool fullyRepresentable = true;
        if (root["required"] is JArray requiredArray)
        {
            foreach (JToken element in requiredArray)
            {
                if (element.Type == JTokenType.String)
                {
                    _ = required.Add((string)element!);
                }
                else
                {
                    fullyRepresentable = false;
                }
            }
        }

        List<SchemaFieldDraft> fields = [];

        foreach (KeyValuePair<string, JToken?> property in properties)
        {
            if (property.Value is not JObject propSchema)
            {
                fullyRepresentable = false;
                continue;
            }

            (SchemaFieldDraft? draft, bool propRepresentable) = TryBuildDraft(property.Key, propSchema, required.Contains(property.Key));
            if (draft is null)
            {
                fullyRepresentable = false;
                continue;
            }

            fields.Add(draft);
            if (!propRepresentable)
            {
                fullyRepresentable = false;
            }
        }

        return new ParseResult(fields, fullyRepresentable);
    }

    private static JObject BuildPropertySchema(SchemaFieldDraft field)
    {
        switch (field.Type)
        {
            case SchemaFieldType.Text:
                JObject text = new() { ["type"] = "string" };
                string[] enumValues = SplitCsv(field.EnumValues);
                if (enumValues.Length > 0)
                {
                    text["enum"] = new JArray(enumValues.Cast<object>().ToArray());
                }

                return text;

            case SchemaFieldType.Number:
                return BuildNumberSchema("number", field);

            case SchemaFieldType.WholeNumber:
                return BuildNumberSchema("integer", field);

            case SchemaFieldType.TrueFalse:
                return new JObject { ["type"] = "boolean" };

            case SchemaFieldType.List:
                return new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = TypeKeyword(field.ListItemType) },
                };

            default:
                return new JObject { ["type"] = "string" };
        }
    }

    private static JObject BuildNumberSchema(string typeKeyword, SchemaFieldDraft field)
    {
        JObject schema = new() { ["type"] = typeKeyword };
        if (field.Min.HasValue)
        {
            schema["minimum"] = field.Min.Value;
        }

        if (field.Max.HasValue)
        {
            schema["maximum"] = field.Max.Value;
        }

        return schema;
    }

    private static string TypeKeyword(SchemaFieldType type)
    {
        return type switch
        {
            SchemaFieldType.Number => "number",
            SchemaFieldType.WholeNumber => "integer",
            SchemaFieldType.TrueFalse => "boolean",
            SchemaFieldType.List => "array",
            _ => "string",
        };
    }

    private static (SchemaFieldDraft? Draft, bool Representable) TryBuildDraft(string name, JObject prop, bool isRequired)
    {
        string? jsonType = (string?)prop["type"];
        bool representable = true;

        SchemaFieldType type;
        switch (jsonType)
        {
            case "string":
                type = SchemaFieldType.Text;
                break;
            case "number":
                type = SchemaFieldType.Number;
                break;
            case "integer":
                type = SchemaFieldType.WholeNumber;
                break;
            case "boolean":
                type = SchemaFieldType.TrueFalse;
                break;
            case "array":
                type = SchemaFieldType.List;
                break;
            default:
                return (null, false);
        }

        SchemaFieldDraft draft = new()
        {
            Name = name,
            Type = type,
            Required = isRequired,
        };

        if (type == SchemaFieldType.Text && prop["enum"] is JArray enumArr)
        {
            draft.EnumValues = string.Join(", ", enumArr.Select(t => (string)t!));
        }

        if (type is SchemaFieldType.Number or SchemaFieldType.WholeNumber)
        {
            if (prop["minimum"] is JValue min)
            {
                draft.Min = min.ToObject<double?>();
            }

            if (prop["maximum"] is JValue max)
            {
                draft.Max = max.ToObject<double?>();
            }
        }

        if (type == SchemaFieldType.List && prop["items"] is JObject items)
        {
            string? itemType = (string?)items["type"];

            // Object items or anything we don't model (null, etc.) are kept as Text in
            // the builder but flagged as not representable so the user knows detail will
            // be lost on round-trip.
            switch (itemType)
            {
                case "number":
                    draft.ListItemType = SchemaFieldType.Number;
                    break;
                case "integer":
                    draft.ListItemType = SchemaFieldType.WholeNumber;
                    break;
                case "boolean":
                    draft.ListItemType = SchemaFieldType.TrueFalse;
                    break;
                case "array":
                    draft.ListItemType = SchemaFieldType.List;
                    break;
                case "string":
                    draft.ListItemType = SchemaFieldType.Text;
                    break;
                default:
                    draft.ListItemType = SchemaFieldType.Text;
                    representable = false;
                    break;
            }
        }

        // Fields with extra constraints we don't model (pattern, minLength, format, etc.)
        // get represented at their type level only.
        if (HasUnsupportedPropertyKeyword(prop))
        {
            representable = false;
        }

        return (draft, representable);
    }

    private static readonly HashSet<string> SupportedRootKeywords = new(StringComparer.Ordinal)
    {
        "type",
        "properties",
        "required",
    };

    private static bool HasUnsupportedKeyword(JObject root)
    {
        foreach (KeyValuePair<string, JToken?> property in root)
        {
            if (!SupportedRootKeywords.Contains(property.Key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUnsupportedPropertyKeyword(JObject prop)
    {
        return prop["pattern"] is not null
            || prop["format"] is not null
            || prop["minLength"] is not null
            || prop["maxLength"] is not null
            || prop["multipleOf"] is not null
            || prop["oneOf"] is not null
            || prop["anyOf"] is not null
            || prop["allOf"] is not null;
    }

    private static string[] SplitCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
