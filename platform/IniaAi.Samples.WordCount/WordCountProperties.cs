using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Samples.WordCount;

/// <summary>The settings of a Count Words block: the text to count, which may name earlier blocks.</summary>
public sealed class WordCountProperties : BlockProperties
{
    public const BlockTypes TYPE = (BlockTypes)1000;

    [JsonProperty(nameof(TextTemplate))]
    public string? TextTemplate { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public WordCountExecutionState? ExecutionState { get; set; }

    public WordCountProperties()
    {
        Type = TYPE;
    }

    public override BlockProperties Clone()
    {
        var clone = new WordCountProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new WordCountExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new WordCountExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
