using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for BuildTopicsProperties. Holds resolved TopicsModel.
/// </summary>
public class BuildTopicsPropertiesExecutionState : BlockPropertiesExecutionStateBase<BuildTopicsPropertiesExecutionState>
{
    [JsonProperty(nameof(TopicsModel), NullValueHandling = NullValueHandling.Ignore)]
    public string? TopicsModel { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal BuildTopicsPropertiesExecutionState()
    {
    }

    public BuildTopicsPropertiesExecutionState(BuildTopicsProperties source)
    {
        TopicsModel = source.TopicsModelTemplate;
    }
}
