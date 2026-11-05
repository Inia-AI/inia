using IniaAi.Common.Reflection;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows;

public class TextInput : Entity<TextInput>, ITemplatable
{
    [JsonIgnore]
    [CloneIgnore]
    public string BlockId { get; set; } = string.Empty;

    [JsonIgnore]
    [CloneIgnore]
    public Block? Block { get; set; }

    [Required]
    [StringLength(450)]
    [JsonProperty(nameof(ReplacementTag))]
    [CloneIgnore]
    public string ReplacementTag
    {
        get;
        set
        {
            if (ReplacementTagIsInUse(value))
            {
                throw new InvalidOperationException(
                    $"The replacement tag '{value}' is already in use for the block.");
            }

            field = value;
        }
    } = string.Empty;

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the text input value. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="TextInputExecutionState.Value"/> for the actual value after
    /// replacements.
    /// </summary>
    [JsonProperty(nameof(Value))]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? Value { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState))]
    public TextInputExecutionState? ExecutionState { get; set; }

    public TextInput()
    {
    }

    public TextInput(Block block, string replacementTag)
    {
        Block = block;
        BlockId = block.Id;
        block.TextInputs.Add(this);
        ReplacementTag = replacementTag;
    }

    public TextInput(Block block, string replacementTag, string? value)
        : this(block, replacementTag)
    {
        Value = value;
    }

    public TextInput CloneToBlock(Block block)
    {
        var clone = new TextInput(block, ReplacementTag, Value);
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public void ReplaceAll(
        Dictionary<string, string?> replacements,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        PlaceholderReflectionHelper.ReplaceAll(this, false, replacements, stringComparison);
    }

    public void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new TextInputExecutionState(this);

        ExecutionState.ReplaceAllPlaceholders(
            asPlaceHolder,
            inputs,
            stringComparison);
    }

    public void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new TextInputExecutionState(this);

        ExecutionState.ReplaceAllPlaceholders(
            asPlaceHolder,
            inputs,
            stringComparison);
    }

    public void ClearExecutionState()
    {
        ExecutionState = null;
    }

    public bool HasPlaceholder(
        string placeholder,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        return PlaceholderReflectionHelper.HasPlaceholder(this, placeholder, stringComparison);
    }

    public List<string> GetAllPlaceholders()
    {
        return [.. PlaceholderReflectionHelper.GetAllPlaceholders(this)];
    }

    private bool ReplacementTagIsInUse(string value)
    {
        return Block is not null
            && Block.TextInputs.Any(ti => ti != this && ti.ReplacementTag == value);
    }
}
