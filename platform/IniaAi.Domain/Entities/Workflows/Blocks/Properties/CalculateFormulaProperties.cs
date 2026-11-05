using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for formula calculation block.
/// </summary>
public class CalculateFormulaProperties : BlockProperties
{
    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the formula. It can contain placeholders that get
    /// replaced at execution time. Use
    /// <see cref="CalculateFormulaPropertiesExecutionState.Formula"/> for
    /// the actual formula after replacements.
    /// </summary>
    /// <remarks>
    /// Supports basic operations (+, -, *, /) and the abs() function.
    /// </remarks>
    [JsonProperty(nameof(FormulaTemplate))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? FormulaTemplate { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public CalculateFormulaPropertiesExecutionState? ExecutionState { get; set; }

    public CalculateFormulaProperties()
    {
        Type = BlockTypes.CalculateNumber;
    }

    public override BlockProperties Clone()
    {
        var clone = new CalculateFormulaProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new CalculateFormulaPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new CalculateFormulaPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
