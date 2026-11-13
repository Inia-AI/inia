using System.Text.Json;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Queries.WorkflowExecutions;

/// <summary>
/// The run variables whose value never leaves the server: a confidential one, which holds a secret, and a hidden one,
/// which is the template's own and which the run fills with the template's default. A run's reader sees neither value.
/// Stored keys are camelCase and replacement tags PascalCase, so tags compare case-insensitively.
/// </summary>
internal static class UnreadableVariables
{
    public static HashSet<string> TagsOf(IEnumerable<WorkflowVariable> definitions)
    {
        return new HashSet<string>(
            definitions.Where(variable => variable.IsConfidential || variable.IsHidden).Select(variable => variable.ReplacementTag),
            StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, JsonElement>? Without(Dictionary<string, JsonElement>? values, IEnumerable<WorkflowVariable> definitions)
    {
        if (values is null || values.Count == 0)
        {
            return values;
        }

        HashSet<string> unreadable = TagsOf(definitions);
        return unreadable.Count == 0
            ? values
            : values.Where(entry => !unreadable.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
    }
}
