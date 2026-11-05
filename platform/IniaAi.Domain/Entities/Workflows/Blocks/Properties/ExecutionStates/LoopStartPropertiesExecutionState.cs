using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for LoopStartProperties. Holds resolved LoopedList.
/// </summary>
public class LoopStartPropertiesExecutionState : BlockPropertiesExecutionStateBase<LoopStartPropertiesExecutionState>
{
    [JsonProperty(nameof(LoopedList), NullValueHandling = NullValueHandling.Ignore)]
    public string? LoopedList { get; set; }

    [JsonProperty(nameof(ListTitle), NullValueHandling = NullValueHandling.Ignore)]
    public string? ListTitle { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal LoopStartPropertiesExecutionState()
    {
    }

    public LoopStartPropertiesExecutionState(LoopStartProperties source)
    {
        LoopedList = source.LoopedListTemplate;
        ListTitle = source.ListTitle;
    }
}
