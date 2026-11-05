using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for MergeJsonProperties. Holds resolved JSON inputs after placeholder replacement.
/// </summary>
public class MergeJsonPropertiesExecutionState : BlockPropertiesExecutionStateBase<MergeJsonPropertiesExecutionState>
{
    [SanitizePlaceholderValues(PlaceholderSanitization.Json)]
    public List<string> JsonInputs { get; set; } = [];

    /// <summary>
    /// Per-input extraction config copied from <see cref="MergeJsonProperties.InputExtractions"/>.
    /// Parallel to <see cref="JsonInputs"/> by index. Missing trailing entries default to <see cref="JsonExtractionMode.Single"/>.
    /// Patterns are copied verbatim from the workflow configuration; placeholders are not resolved here.
    /// </summary>
    [JsonProperty(nameof(InputExtractions))]
    public List<MergeJsonInputExtraction> InputExtractions { get; set; } = [];

    /// <summary>
    /// Pre-parse find/replace transforms copied from <see cref="MergeJsonProperties.PreParseTransforms"/>.
    /// Applied to every input (and extracted snippet) before JSON parsing. Patterns are copied verbatim
    /// from the workflow configuration; placeholders are not resolved here.
    /// </summary>
    [JsonProperty(nameof(PreParseTransforms))]
    public List<MergeJsonPreParseTransform> PreParseTransforms { get; set; } = [];

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal MergeJsonPropertiesExecutionState()
    {
    }

    public MergeJsonPropertiesExecutionState(MergeJsonProperties source)
    {
        JsonInputs = source.JsonInputs is null ? [] : [.. source.JsonInputs];
        InputExtractions = source.InputExtractions is null ? [] : [.. source.InputExtractions.Select(e => e.Clone())];
        PreParseTransforms = source.PreParseTransforms is null ? [] : [.. source.PreParseTransforms.Select(t => t.Clone())];
    }
}
