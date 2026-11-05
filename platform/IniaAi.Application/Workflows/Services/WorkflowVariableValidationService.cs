using ErrorOr;
using IniaAi.Application.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Text.Json;

namespace IniaAi.Application.Workflows.Services;

/// <summary>
/// Service for validating workflow variables.
/// </summary>
public class WorkflowVariableValidationService : IWorkflowVariableValidationService
{
    /// <summary>
    /// The metadata key on every error <see cref="ValidateVariables"/> returns that names the
    /// variable's replacement tag, so a caller can tell which variable failed without reading the
    /// message.
    /// </summary>
    public const string REPLACEMENT_TAG_METADATA_KEY = "replacementTag";

    /// <summary>
    /// Validates workflow variables against their definitions.
    /// </summary>
    /// <param name="workflow">The workflow containing variable definitions.</param>
    /// <param name="variables">The variable values to validate.</param>
    /// <returns>A list of validation errors, if any.</returns>
    public List<Error> ValidateVariables(Workflow workflow, Dictionary<string, JsonElement>? variables)
    {
        var errors = new List<Error>();

        // Case-insensitive fold; indexer (not the copy-ctor, which throws on twin keys).
        if (variables is { Count: > 0 })
        {
            var normalized = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach ((string key, JsonElement value) in variables)
            {
                normalized[key] = value;
            }

            variables = normalized;
        }
        else
        {
            variables = [];
        }

        foreach (WorkflowVariable variable in workflow.Variables)
        {
            // Check required variables
            if (variable.IsRequired && !variables.ContainsKey(variable.ReplacementTag))
            {
                errors.Add(VariableError(
                    variable,
                    "WorkflowVariable.Required",
                    $"Required variable '{variable.DisplayName}' ({variable.ReplacementTag}) is missing."));
                continue;
            }

            // Skip validation if variable is not provided and not required
            if (!variables.TryGetValue(variable.ReplacementTag, out JsonElement value))
            {
                continue;
            }

            // Validate variable type
            switch (variable.Type)
            {
                case WorkflowVariableTypes.Dropdown:
                    if (value.ValueKind is not JsonValueKind.String)
                    {
                        errors.Add(VariableError(
                            variable,
                            "WorkflowVariable.InvalidType",
                            $"Dropdown variable '{variable.DisplayName}' ({variable.ReplacementTag}) must be a string."));
                        break;
                    }

                    string? dropdownValue = value.GetString();

                    if (string.IsNullOrWhiteSpace(dropdownValue))
                    {
                        if (variable.IsRequired)
                        {
                            errors.Add(VariableError(
                                variable,
                                "WorkflowVariable.Required",
                                $"Required dropdown variable '{variable.DisplayName}' ({variable.ReplacementTag}) is missing or empty."));
                        }

                        break;
                    }

                    if (!variable.Options.ContainsKey(dropdownValue))
                    {
                        errors.Add(VariableError(
                            variable,
                            "WorkflowVariable.InvalidOption",
                            $"Invalid option '{dropdownValue}' for variable '{variable.DisplayName}' ({variable.ReplacementTag})."));
                    }

                    break;

                case WorkflowVariableTypes.MultiSelect:
                    if (value.ValueKind is not JsonValueKind.Array)
                    {
                        errors.Add(VariableError(
                            variable,
                            "WorkflowVariable.InvalidType",
                            $"MultiSelect variable '{variable.DisplayName}' ({variable.ReplacementTag}) must be an array."));
                        break;
                    }

                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (item.ValueKind is not JsonValueKind.String)
                        {
                            errors.Add(VariableError(
                                variable,
                                "WorkflowVariable.InvalidType",
                                $"MultiSelect items for '{variable.DisplayName}' ({variable.ReplacementTag}) must be strings."));
                            break;
                        }

                        if (!variable.Options.ContainsKey(item.GetString()!))
                        {
                            errors.Add(VariableError(
                                variable,
                                "WorkflowVariable.InvalidOption",
                                $"Invalid option '{item.GetString()}' for variable '{variable.DisplayName}' ({variable.ReplacementTag})."));
                        }
                    }

                    break;

                case WorkflowVariableTypes.Number:
                    if (value.ValueKind is not JsonValueKind.Number)
                    {
                        errors.Add(VariableError(
                            variable,
                            "WorkflowVariable.InvalidType",
                            $"Number variable '{variable.DisplayName}' ({variable.ReplacementTag}) must be a number."));
                    }

                    break;

                case WorkflowVariableTypes.DateTime:
                    if (value.ValueKind is not JsonValueKind.String)
                    {
                        errors.Add(VariableError(
                            variable,
                            "WorkflowVariable.InvalidType",
                            $"DateTime variable '{variable.DisplayName}' ({variable.ReplacementTag}) must be a string."));
                        break;
                    }

                    if (!WorkflowVariable.TryParseDate(value.GetString(), out _))
                    {
                        errors.Add(VariableError(
                            variable,
                            "WorkflowVariable.InvalidDateTime",
                            $"Invalid date/time value '{value.GetString()}' for variable '{variable.DisplayName}' ({variable.ReplacementTag})."));
                    }

                    break;

                case WorkflowVariableTypes.List:
                    if (value.ValueKind is not JsonValueKind.Array)
                    {
                        errors.Add(VariableError(
                            variable,
                            "WorkflowVariable.InvalidType",
                            $"List variable '{variable.DisplayName}' ({variable.ReplacementTag}) must be an array."));
                        break;
                    }

                    if (variable.IsRequired && value.GetArrayLength() == 0)
                    {
                        errors.Add(VariableError(
                            variable,
                            "WorkflowVariable.InvalidList",
                            $"List value cannot be empty for required variable '{variable.DisplayName}' ({variable.ReplacementTag})."));
                    }

                    break;

                case WorkflowVariableTypes.String:
                    if (value.ValueKind is not JsonValueKind.String)
                    {
                        errors.Add(VariableError(
                            variable,
                            "WorkflowVariable.InvalidType",
                            $"String variable '{variable.DisplayName}' ({variable.ReplacementTag}) must be a string."));
                    }

                    break;
            }
        }

        return errors;
    }

    private static Error VariableError(WorkflowVariable variable, string code, string description)
    {
        return Error.Validation(
            code,
            description,
            new Dictionary<string, object> { [REPLACEMENT_TAG_METADATA_KEY] = variable.ReplacementTag });
    }

    /// <summary>
    /// Converts workflow variable values to strings for job inputs.
    /// </summary>
    /// <param name="variables">The variable values to convert.</param>
    /// <returns>A dictionary of string values ready for job inputs.</returns>
    public Dictionary<string, string> ConvertVariablesToStrings(Dictionary<string, JsonElement>? variables)
    {
        var result = new Dictionary<string, string>();
        if (variables is null)
        {
            return result;
        }

        foreach ((string key, JsonElement value) in variables)
        {
            string stringValue = value.ValueKind switch
            {
                JsonValueKind.Array => string.Join("|", value.EnumerateArray().Select(x => x.GetString() ?? string.Empty)),
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetDouble().ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.ToString(),
            };

            if (string.Equals(key, "CompanyURL", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(stringValue);
                    stringValue = uri.Host;
                }
                catch (UriFormatException)
                {
                    string[] parts = stringValue.Split('/', StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length > 0 && parts[0].Contains('.'))
                    {
                        stringValue = parts[0];
                    }
                }
            }

            result[key] = stringValue;
        }

        return result;
    }

    /// <summary>
    /// Converts workflow variable values to objects for job inputs.
    /// </summary>
    /// <param name="variables">The variable values to convert.</param>
    /// <returns>A dictionary of object values ready for job inputs.</returns>
    public Dictionary<string, object> ConvertVariablesToObjects(Dictionary<string, JsonElement>? variables)
    {
        var result = new Dictionary<string, object>();
        if (variables is null)
        {
            return result;
        }

        foreach ((string key, JsonElement value) in variables)
        {
            result[key] = ConvertJsonElement(value);
        }

        return result;
    }

    private object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                prop => prop.Name,
                prop => ConvertJsonElement(prop.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString(),
        };
    }
}
