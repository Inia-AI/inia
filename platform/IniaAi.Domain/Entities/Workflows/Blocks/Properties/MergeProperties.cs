using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for merging multiple text inputs using a template.
/// </summary>
public class MergeProperties : BlockProperties, IHasSignatureFields
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the merged text. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="MergePropertiesExecutionState.Text"/> for the actual
    /// merged text after replacements.
    /// </summary>
    [JsonProperty(nameof(TextTemplate))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TextTemplate { get; set; }

    /// <summary>
    /// Staff-authored signature field definitions for this block. The key is the
    /// placeholder name used in <see cref="TextTemplate"/>; resolved values flow through
    /// the standard <c>ReplacePlaceholders</c> pipeline at execution time. Active client
    /// override values live in the <c>SignatureFieldOverride</c> table — they are never
    /// serialised onto the block JSON.
    /// </summary>
    [JsonProperty(nameof(SignatureFields), NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, SignatureFieldDefinition>? SignatureFields { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public MergePropertiesExecutionState? ExecutionState { get; set; }

    public MergeProperties()
    {
        Type = BlockTypes.Merge;
    }

    public override BlockProperties Clone()
    {
        var clone = new MergeProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    /// <summary>
    /// Merge has a single template (<see cref="TextTemplate"/>) into which all
    /// placeholders are substituted at execution time.
    /// </summary>
    public IEnumerable<string?> GetTemplateSources()
    {
        yield return TextTemplate;
    }

    /// <summary>Single template, so ordering scans the same <see cref="TextTemplate"/>.</summary>
    public IEnumerable<string?> GetSignatureOrderingTemplates()
    {
        yield return TextTemplate;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new MergePropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new MergePropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
