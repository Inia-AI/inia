using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

public class LoopStartProperties : BlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the list title. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="LoopStartPropertiesExecutionState.ListTitle"/> for the
    /// actual list title after replacements.
    /// </summary>
    [JsonProperty(nameof(ListTitle))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? ListTitle { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the looped list. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="LoopStartPropertiesExecutionState.LoopedList"/> for the
    /// actual looped list after replacements.
    /// </summary>
    [JsonProperty(nameof(LoopedListTemplate))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? LoopedListTemplate { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public LoopStartPropertiesExecutionState? ExecutionState { get; set; }

    public LoopStartProperties()
    {
        Type = BlockTypes.LoopStart;
    }

    public override BlockProperties Clone()
    {
        var clone = new LoopStartProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new LoopStartPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new LoopStartPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
