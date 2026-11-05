using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.JsonSchemas;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.OutputFormat;

/// <summary>
/// Validates a model response against an AI Query block's configured output format.
/// Pure function, no I/O. The caller is expected to have already run
/// <see cref="LenientCleanup.Clean"/> on the response.
/// </summary>
public static class OutputFormatValidator
{
    public static ValidationResult Validate(string cleaned, AiQueryProperties props)
    {
        switch (props.OutputFormatType)
        {
            case OutputFormatType.None:
                return ValidationResult.Valid();

            case OutputFormatType.SingleTag:
                return ValidateSingleTag(cleaned, props.OutputFormatTagName);

            case OutputFormatType.MultiTag:
                return ValidateMultiTag(cleaned, props.OutputFormatTagNames);

            case OutputFormatType.Json:
                return ValidateJson(cleaned, props.OutputFormatJsonSchema);

            case OutputFormatType.JsonInTag:
                return ValidateJsonInTag(cleaned, props.OutputFormatTagName, props.OutputFormatJsonSchema);

            default:
                return ValidationResult.Invalid($"Unsupported output format type: {props.OutputFormatType}");
        }
    }

    public static ValidationResult ValidateSingleTag(string cleaned, string? tagName)
    {
        if (!TagMatcher.IsValidTagName(tagName))
        {
            return ValidationResult.Invalid("Tag name is missing or invalid.");
        }

        string? inner = TagMatcher.FindFirstTagContent(cleaned, tagName!);
        if (inner is null)
        {
            return ValidationResult.Invalid($"Response does not contain a <{tagName}>...</{tagName}> tag.");
        }

        return ValidationResult.Valid();
    }

    public static ValidationResult ValidateMultiTag(string cleaned, string? csvTagNames)
    {
        IReadOnlyList<string> names = TagMatcher.SplitTagNames(csvTagNames);
        if (names.Count == 0)
        {
            return ValidationResult.Invalid("Multi-tag format requires at least one tag name.");
        }

        List<string> missing = [];
        foreach (string name in names)
        {
            if (!TagMatcher.IsValidTagName(name))
            {
                return ValidationResult.Invalid($"Tag name '{name}' is invalid.");
            }

            if (TagMatcher.FindFirstTagContent(cleaned, name) is null)
            {
                missing.Add(name);
            }
        }

        if (missing.Count > 0)
        {
            return ValidationResult.Invalid($"Response is missing the following tags: {string.Join(", ", missing.Select(m => $"<{m}>"))}.");
        }

        return ValidationResult.Valid();
    }

    public static ValidationResult ValidateJson(string cleaned, string? schemaSource)
    {
        JToken token;
        try
        {
            token = JToken.Parse(cleaned);
        }
        catch (Exception ex)
        {
            return ValidationResult.Invalid($"Response is not valid JSON: {ex.Message}");
        }

        return ValidateAgainstSchema(token, schemaSource);
    }

    public static ValidationResult ValidateJsonInTag(string cleaned, string? tagName, string? schemaSource)
    {
        if (!TagMatcher.IsValidTagName(tagName))
        {
            return ValidationResult.Invalid("Tag name is missing or invalid.");
        }

        string? inner = TagMatcher.FindFirstTagContent(cleaned, tagName!);
        if (inner is null)
        {
            return ValidationResult.Invalid($"Response does not contain a <{tagName}>...</{tagName}> tag.");
        }

        string innerCleaned = LenientCleanup.Clean(inner);
        return ValidateJson(innerCleaned, schemaSource);
    }

    private static ValidationResult ValidateAgainstSchema(JToken token, string? schemaSource)
    {
        if (string.IsNullOrWhiteSpace(schemaSource))
        {
            return ValidationResult.Valid();
        }

        CompiledJsonSchema schema;
        try
        {
            schema = JsonSchemaEvaluation.CompileSchema(schemaSource);
        }
        catch (Exception ex)
        {
            return ValidationResult.Invalid($"Configured JSON Schema is invalid: {ex.Message}");
        }

        IReadOnlyList<string> errors = JsonSchemaEvaluation.FindViolations(schema, token);

        if (errors.Count > 0)
        {
            return ValidationResult.Invalid($"Response does not match JSON Schema: {string.Join("; ", errors)}");
        }

        return ValidationResult.Valid();
    }
}
