using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

public class MergeJsonProperties : BlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the JSON inputs to merge. Values can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="MergeJsonPropertiesExecutionState.JsonInputs"/> for the
    /// resolved JSON inputs after replacements.
    /// </summary>
    [JsonProperty(nameof(JsonInputs))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public List<string> JsonInputs { get; set; } = [];

    public bool ShouldSerializeJsonInputs()
    {
        return JsonInputs is { Count: > 0 };
    }

    /// <summary>
    /// Per-input extraction config, parallel to <see cref="JsonInputs"/> by index.
    /// When an entry is missing or has Mode=Single, the input is parsed as one JSON (the original behavior).
    /// When Mode is XmlTagPattern or Regex, the resolved input value is sliced into multiple JSON snippets,
    /// each of which feeds the same deep-merge.
    /// </summary>
    [JsonProperty(nameof(InputExtractions))]
    public List<MergeJsonInputExtraction> InputExtractions { get; set; } = [];

    public bool ShouldSerializeInputExtractions()
    {
        return InputExtractions is { Count: > 0 };
    }

    /// <summary>
    /// Find/replace transforms applied to every resolved input (and every extracted snippet)
    /// <b>before</b> JSON parsing. Used to sanitise inputs that would otherwise be invalid JSON —
    /// e.g. stripping digit-group separators. Empty (the default) means inputs are parsed verbatim,
    /// preserving the original behavior. Patterns are static configuration, not template-resolved.
    /// </summary>
    [JsonProperty(nameof(PreParseTransforms))]
    public List<MergeJsonPreParseTransform> PreParseTransforms { get; set; } = [];

    public bool ShouldSerializePreParseTransforms()
    {
        return PreParseTransforms is { Count: > 0 };
    }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public MergeJsonPropertiesExecutionState? ExecutionState { get; set; }

    public MergeJsonProperties()
    {
        Type = BlockTypes.MergeJson;
    }

    public override BlockProperties Clone()
    {
        var clone = new MergeJsonProperties();
        CloneHelper.CopyScalars(this, clone);
        clone.InputExtractions = InputExtractions is null ? [] : [.. InputExtractions.Select(e => e.Clone())];
        clone.PreParseTransforms = PreParseTransforms is null ? [] : [.. PreParseTransforms.Select(t => t.Clone())];
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new MergeJsonPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new MergeJsonPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
