using System.Text.Json;

namespace IniaAi.Application.Queries.WorkflowExecutions;

public static class ExecutionVariableJson
{
    /// <summary>
    /// An execution stores its variables as plain objects, and the API returns each as JSON. An
    /// execution without variables returns an empty set, not null.
    /// </summary>
    public static Dictionary<string, JsonElement> From(Dictionary<string, object>? variables)
    {
        return variables?.ToDictionary(variable => variable.Key, variable => JsonSerializer.SerializeToElement(variable.Value)) ?? [];
    }
}
