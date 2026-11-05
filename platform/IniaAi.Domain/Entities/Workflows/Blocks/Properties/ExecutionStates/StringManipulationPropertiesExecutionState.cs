using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for StringManipulationProperties. Holds resolved templated inputs and the operation output.
/// </summary>
public class StringManipulationPropertiesExecutionState : BlockPropertiesExecutionStateBase<StringManipulationPropertiesExecutionState>
{
    [JsonProperty(nameof(Input), NullValueHandling = NullValueHandling.Ignore)]
    public string? Input { get; set; }

    [JsonProperty(nameof(Find), NullValueHandling = NullValueHandling.Ignore)]
    public string? Find { get; set; }

    [JsonProperty(nameof(ReplaceWith), NullValueHandling = NullValueHandling.Ignore)]
    public string? ReplaceWith { get; set; }

    [JsonProperty(nameof(Separator), NullValueHandling = NullValueHandling.Ignore)]
    public string? Separator { get; set; }

    [JsonProperty(nameof(TrimCharacters), NullValueHandling = NullValueHandling.Ignore)]
    public string? TrimCharacters { get; set; }

    [JsonProperty(nameof(Output), NullValueHandling = NullValueHandling.Ignore)]
    public string? Output { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal StringManipulationPropertiesExecutionState()
    {
    }

    public StringManipulationPropertiesExecutionState(StringManipulationProperties source)
    {
        Input = source.InputTemplate;
        Find = source.Find;
        ReplaceWith = source.ReplaceWith;
        Separator = source.Separator;
        TrimCharacters = source.TrimCharacters;
    }
}
