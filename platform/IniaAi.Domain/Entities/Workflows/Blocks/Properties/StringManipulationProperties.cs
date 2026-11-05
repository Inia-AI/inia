using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for the StringManipulation block.
/// One operation per block, selected via <see cref="Operation"/>; parameters not used by the selected operation are ignored.
/// </summary>
public class StringManipulationProperties : BlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the input string. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="StringManipulationPropertiesExecutionState.Input"/> for the
    /// actual input after replacements.
    /// </summary>
    [JsonProperty(nameof(InputTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? InputTemplate { get; set; }

    /// <summary>
    /// The one text operation the block applies. Settings the operation does not use are ignored.
    /// </summary>
    [JsonProperty(nameof(Operation))]
    [JsonConverter(typeof(StringEnumConverter))]
    public StringManipulationOperation Operation { get; set; } = StringManipulationOperation.Replace;

    // Replace + ReplaceUsingPattern
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the find pattern. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="StringManipulationPropertiesExecutionState.Find"/> for the
    /// actual find pattern after replacements.
    /// </summary>
    [JsonProperty(nameof(Find), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? Find { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the replacement string. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="StringManipulationPropertiesExecutionState.ReplaceWith"/>
    /// for the actual replacement string after replacements.
    /// </summary>
    [JsonProperty(nameof(ReplaceWith), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? ReplaceWith { get; set; }

    /// <summary>
    /// Whether Replace, Replace Using Pattern and Distinct tell upper and lower case apart.
    /// </summary>
    [JsonProperty(nameof(CaseSensitive))]
    public bool CaseSensitive { get; set; } = true;

    // Substring
    /// <summary>
    /// Substring: the position to start from, counting from 0. A start past the end gives empty text.
    /// </summary>
    [JsonProperty(nameof(StartIndex), NullValueHandling = NullValueHandling.Ignore)]
    public int? StartIndex { get; set; }

    /// <summary>
    /// Substring: how many characters to keep from StartIndex. Empty keeps the rest of the text.
    /// </summary>
    [JsonProperty(nameof(Length), NullValueHandling = NullValueHandling.Ignore)]
    public int? Length { get; set; }

    // Trim
    /// <summary>
    /// Trim: which ends to trim, Start, End or Both.
    /// </summary>
    [JsonProperty(nameof(TrimSide))]
    [JsonConverter(typeof(StringEnumConverter))]
    public StringManipulationTrimSide TrimSide { get; set; } = StringManipulationTrimSide.Both;

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the trim characters. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="StringManipulationPropertiesExecutionState.TrimCharacters"/>
    /// for the actual trim characters after replacements.
    /// </summary>
    [JsonProperty(nameof(TrimCharacters), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TrimCharacters { get; set; }

    // Split / Join
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the separator string. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="StringManipulationPropertiesExecutionState.Separator"/> for
    /// the actual separator after replacements.
    /// </summary>
    /// <remarks>
    /// Used by both Split and Join operations; doubles as the Split delimiter
    /// (labeled differently in the UI).
    /// </remarks>
    [JsonProperty(nameof(Separator), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? Separator { get; set; }

    /// <summary>
    /// Split: which part to keep, counting from 0; a negative number counts from the end. Empty outputs every part as a JSON list.
    /// </summary>
    [JsonProperty(nameof(SplitIndex), NullValueHandling = NullValueHandling.Ignore)]
    public int? SplitIndex { get; set; }

    // ChangeCase
    /// <summary>
    /// Change Case: Upper, Lower, Title or Sentence case.
    /// </summary>
    [JsonProperty(nameof(CaseType))]
    [JsonConverter(typeof(StringEnumConverter))]
    public StringManipulationCaseType CaseType { get; set; } = StringManipulationCaseType.Lower;

    // Escape / Unescape — shared toggle set
    /// <summary>
    /// Escape and Unescape: whether new lines are escaped (or unescaped).
    /// </summary>
    [JsonProperty(nameof(EscapeNewlines))]
    public bool EscapeNewlines { get; set; } = true;

    /// <summary>
    /// Escape and Unescape: whether tabs are escaped (or unescaped).
    /// </summary>
    [JsonProperty(nameof(EscapeTabs))]
    public bool EscapeTabs { get; set; } = true;

    /// <summary>
    /// Escape and Unescape: whether carriage returns are escaped (or unescaped).
    /// </summary>
    [JsonProperty(nameof(EscapeCarriageReturns))]
    public bool EscapeCarriageReturns { get; set; } = true;

    /// <summary>
    /// Escape and Unescape: whether backslashes are escaped (or unescaped).
    /// </summary>
    [JsonProperty(nameof(EscapeBackslashes))]
    public bool EscapeBackslashes { get; set; } = true;

    /// <summary>
    /// Escape and Unescape: whether double quotes are escaped (or unescaped).
    /// </summary>
    [JsonProperty(nameof(EscapeDoubleQuotes))]
    public bool EscapeDoubleQuotes { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public StringManipulationPropertiesExecutionState? ExecutionState { get; set; }

    public StringManipulationProperties()
    {
        Type = BlockTypes.StringManipulation;
    }

    public override BlockProperties Clone()
    {
        var clone = new StringManipulationProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new StringManipulationPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new StringManipulationPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
