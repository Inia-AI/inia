using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// Holds execution-time state for a Workflow, such as runtime-modified properties.
/// </summary>
public class WorkflowExecutionState : JsonSerializable<WorkflowExecutionState>
{
    [JsonProperty(nameof(TextInput))]
    public string? TextInput { get; set; }

    [JsonConstructor]
    internal WorkflowExecutionState()
    {
    }

    /// <summary>
    /// Initializes execution state from the provided workflow.
    /// </summary>
    /// <param name="workflow">The workflow to capture state from.</param>
    public WorkflowExecutionState(Workflow workflow)
    {
        TextInput = workflow.TextInputTemplate;
    }

    public WorkflowExecutionState(Section section)
    {
        TextInput = section.TextInputTemplate;
    }
}
