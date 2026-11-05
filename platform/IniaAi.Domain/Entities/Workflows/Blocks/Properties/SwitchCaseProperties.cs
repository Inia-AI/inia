using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for the SwitchCase block.
/// Maps an input string to one of several predefined content values.
/// </summary>
public class SwitchCaseProperties : BlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the case-to-value map. Values can contain placeholders
    /// that get replaced at execution time. Use
    /// <see cref="SwitchCasePropertiesExecutionState.Cases"/> for the
    /// actual case map after replacements.
    /// </summary>
    /// <remarks>
    /// Keys are matched against the input case-insensitively after trimming.
    /// Values are the content to output when matched.
    /// </remarks>
    [JsonProperty(nameof(Cases))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Dictionary<string, string> Cases { get; set; } = [];

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the default value. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="SwitchCasePropertiesExecutionState.DefaultValue"/> for
    /// the actual default value after replacements.
    /// </summary>
    [JsonProperty(nameof(DefaultValue), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the switch input. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="SwitchCasePropertiesExecutionState.Input"/> for the
    /// actual input after replacements.
    /// </summary>
    [JsonProperty(nameof(InputTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? InputTemplate { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public SwitchCasePropertiesExecutionState? ExecutionState { get; set; }

    public SwitchCaseProperties()
    {
        Type = BlockTypes.SwitchCase;
    }

    public override BlockProperties Clone()
    {
        var clone = new SwitchCaseProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new SwitchCasePropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new SwitchCasePropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
