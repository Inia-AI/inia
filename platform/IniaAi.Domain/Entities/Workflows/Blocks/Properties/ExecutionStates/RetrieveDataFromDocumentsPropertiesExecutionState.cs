using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for RetrieveDataFromDocumentsProperties. Empty — no templatable fields.
/// </summary>
public class RetrieveDataFromDocumentsPropertiesExecutionState : BlockPropertiesExecutionStateBase<RetrieveDataFromDocumentsPropertiesExecutionState>
{
    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal RetrieveDataFromDocumentsPropertiesExecutionState()
    {
    }

    public RetrieveDataFromDocumentsPropertiesExecutionState(RetrieveDataFromDocumentsProperties source)
    {
    }
}
