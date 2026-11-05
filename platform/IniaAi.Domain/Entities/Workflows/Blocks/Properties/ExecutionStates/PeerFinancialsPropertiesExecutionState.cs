using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for PeerFinancialsProperties. Holds the resolved target periods,
/// currency and units.
/// </summary>
public class PeerFinancialsPropertiesExecutionState
    : BlockPropertiesExecutionStateBase<PeerFinancialsPropertiesExecutionState>
{
    [JsonProperty(nameof(TargetPeriodsJson), NullValueHandling = NullValueHandling.Ignore)]
    public string? TargetPeriodsJson { get; set; }

    [JsonProperty(nameof(TargetCurrency), NullValueHandling = NullValueHandling.Ignore)]
    public string? TargetCurrency { get; set; }

    [JsonProperty(nameof(TargetUnits), NullValueHandling = NullValueHandling.Ignore)]
    public string? TargetUnits { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal PeerFinancialsPropertiesExecutionState()
    {
    }

    public PeerFinancialsPropertiesExecutionState(PeerFinancialsProperties source)
    {
        TargetPeriodsJson = source.TargetPeriodsJson;
        TargetCurrency = source.TargetCurrency;
        TargetUnits = source.TargetUnits;
    }
}
