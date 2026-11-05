using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for MergeProperties. Holds resolved Text.
/// </summary>
public class MergePropertiesExecutionState : BlockPropertiesExecutionStateBase<MergePropertiesExecutionState>
{
    [JsonProperty(nameof(Text), NullValueHandling = NullValueHandling.Ignore)]
    public string? Text { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal MergePropertiesExecutionState()
    {
    }

    public MergePropertiesExecutionState(MergeProperties source)
    {
        Text = source.TextTemplate;
    }
}
