using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for CalculateFormulaProperties. Holds resolved Formula.
/// </summary>
public class CalculateFormulaPropertiesExecutionState : BlockPropertiesExecutionStateBase<CalculateFormulaPropertiesExecutionState>
{
    [JsonProperty(nameof(Formula), NullValueHandling = NullValueHandling.Ignore)]
    public string? Formula { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal CalculateFormulaPropertiesExecutionState()
    {
    }

    public CalculateFormulaPropertiesExecutionState(CalculateFormulaProperties source)
    {
        Formula = source.FormulaTemplate;
    }
}
