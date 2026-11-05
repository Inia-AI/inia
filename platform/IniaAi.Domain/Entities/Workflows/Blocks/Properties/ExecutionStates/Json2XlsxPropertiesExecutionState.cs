using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for Json2XlsxProperties. Holds resolved JsonInput and OutputFileName.
/// </summary>
public class Json2XlsxPropertiesExecutionState : BlockPropertiesExecutionStateBase<Json2XlsxPropertiesExecutionState>
{
    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    [JsonProperty(nameof(JsonInput), NullValueHandling = NullValueHandling.Ignore)]
    public string? JsonInput { get; set; }

    [JsonProperty(nameof(OutputFileName), NullValueHandling = NullValueHandling.Ignore)]
    public string? OutputFileName { get; set; }

    [JsonProperty(nameof(ConversionType), NullValueHandling = NullValueHandling.Ignore)]
    public string? ConversionType { get; set; }

    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    [JsonProperty(nameof(MappingJson), NullValueHandling = NullValueHandling.Ignore)]
    public string? MappingJson { get; set; }

    [JsonProperty(nameof(CreatesDocumentClass), NullValueHandling = NullValueHandling.Ignore)]
    public string? CreatesDocumentClass { get; set; }

    [JsonProperty(nameof(InputDocumentClass), NullValueHandling = NullValueHandling.Ignore)]
    public string? InputDocumentClass { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal Json2XlsxPropertiesExecutionState()
    {
    }

    public Json2XlsxPropertiesExecutionState(Json2XlsxProperties source)
    {
        JsonInput = source.JsonInputTemplate;
        OutputFileName = source.OutputFileName;
        ConversionType = source.ConversionType;
        MappingJson = source.MappingJson;
        CreatesDocumentClass = source.CreatesDocumentClass;
        InputDocumentClass = source.InputDocumentClass;
    }
}
