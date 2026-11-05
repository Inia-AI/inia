using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for SwitchCaseProperties. Holds resolved Cases dictionary, DefaultValue, and Input.
/// </summary>
public class SwitchCasePropertiesExecutionState : BlockPropertiesExecutionStateBase<SwitchCasePropertiesExecutionState>
{
    [JsonProperty(nameof(Cases))]
    public Dictionary<string, string> Cases { get; set; } = [];

    public bool ShouldSerializeCases()
    {
        return Cases.Count > 0;
    }

    [JsonProperty(nameof(DefaultValue), NullValueHandling = NullValueHandling.Ignore)]
    public string? DefaultValue { get; set; }

    [JsonProperty(nameof(Input), NullValueHandling = NullValueHandling.Ignore)]
    public string? Input { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal SwitchCasePropertiesExecutionState()
    {
    }

    public SwitchCasePropertiesExecutionState(SwitchCaseProperties source)
    {
        Cases = new Dictionary<string, string>(source.Cases);
        DefaultValue = source.DefaultValue;
        Input = source.InputTemplate;
    }
}
