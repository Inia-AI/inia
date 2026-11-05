using System.Globalization;
using System.Text.Json;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;

internal static class WorkflowExecutionVariableResolver
{
    internal const string CONFIDENTIAL_MASK = "••••";
    private const string TYPE_STRING = nameof(WorkflowVariableTypes.String);
    private const string TYPE_NUMBER = nameof(WorkflowVariableTypes.Number);
    private const string TYPE_LIST = nameof(WorkflowVariableTypes.List);

    private static readonly Dictionary<string, string> NO_OPTIONS = [];

    public static List<WorkflowExecutionVariableValueDto> Resolve(
        IEnumerable<WorkflowVariable> definitions,
        IReadOnlyDictionary<string, JsonElement>? rawValues)
    {
        var resolved = new List<WorkflowExecutionVariableValueDto>();
        var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Persisted value keys are camelCase while a definition's ReplacementTag is
        // PascalCase, so match them case-insensitively — otherwise every variable
        // yields a valueless definition row plus an orphan row holding the value.
        // Built with a loop rather than the dictionary ctor so case-colliding
        // legacy keys overwrite instead of throwing on this read path.
        var valuesByTag = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (rawValues is not null)
        {
            foreach ((string key, JsonElement value) in rawValues)
            {
                valuesByTag[key] = value;
            }
        }

        foreach (WorkflowVariable definition in definitions)
        {
            _ = seenTags.Add(definition.ReplacementTag);

            JsonElement? rawValue =
                valuesByTag.TryGetValue(definition.ReplacementTag, out JsonElement value)
                ? value
                : null;
            string? displayValue = ResolveDisplayValue(definition, rawValue);

            resolved.Add(new WorkflowExecutionVariableValueDto
            {
                ReplacementTag = definition.ReplacementTag,
                DisplayName = string.IsNullOrEmpty(definition.DisplayName)
                    ? definition.ReplacementTag
                    : definition.DisplayName,
                Description = string.IsNullOrEmpty(definition.Description) ? null : definition.Description,
                Type = definition.Type.ToString(),
                DisplayValue = displayValue,
                IsConfidential = definition.IsConfidential,
                IsHidden = definition.IsHidden,
                IsRequired = definition.IsRequired,
            });
        }

        if (rawValues is null)
        {
            return resolved;
        }

        foreach ((string key, JsonElement value) in rawValues)
        {
            if (seenTags.Contains(key))
            {
                continue;
            }

            string? displayValue = value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : WorkflowVariableDisplay.ValueOf(WorkflowVariableTypes.String, NO_OPTIONS, value);
            resolved.Add(new WorkflowExecutionVariableValueDto
            {
                ReplacementTag = key,
                DisplayName = key,
                Description = null,
                Type = OrphanType(value),
                DisplayValue = displayValue,
                IsConfidential = false,
                IsHidden = false,
                IsRequired = false,
            });
        }

        return resolved;
    }

    private static string? ResolveDisplayValue(WorkflowVariable definition, JsonElement? rawValue)
    {
        if (!WorkflowVariableDisplay.HasValue(rawValue))
        {
            return null;
        }

        if (definition.IsConfidential)
        {
            return CONFIDENTIAL_MASK;
        }

        return WorkflowVariableDisplay.ValueOf(definition.Type, definition.Options, rawValue!.Value);
    }

    private static string OrphanType(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number => TYPE_NUMBER,
            JsonValueKind.Array => TYPE_LIST,
            _ => TYPE_STRING,
        };
    }
}
