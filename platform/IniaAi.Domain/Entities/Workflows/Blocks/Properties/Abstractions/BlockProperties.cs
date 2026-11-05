using IniaAi.Common.Reflection;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

public abstract class BlockProperties : ITemplatable
{
    [JsonProperty(nameof(Type))]
    [JsonConverter(typeof(StringEnumConverter))]
    public BlockTypes Type { get; set; }

    /// <summary>
    /// Per-block override of whether this block's failure aborts the whole execution. Null — the
    /// state of every template authored before the flag existed — defers to the block type's
    /// default, resolved by <see cref="Blocks.Block.AbortsExecutionOnFailure"/>.
    /// Lives here rather than beside the other per-block flags on <see cref="Blocks.Block"/>
    /// because those are real columns: this one rides the Properties JSON and so needed no
    /// migration in either context or provider.
    /// </summary>
    [JsonProperty(nameof(IsCritical), NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsCritical { get; set; }

    /// <summary>
    /// Creates a deep copy of the properties object.
    /// Must be implemented by derived classes to ensure proper cloning of collection properties.
    /// </summary>
    public abstract BlockProperties Clone();

    public bool HasPlaceholder(
        string placeholder,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        return PlaceholderReflectionHelper.HasPlaceholder(this, placeholder, stringComparison);
    }

    public virtual List<string> GetAllPlaceholders()
    {
        return [.. PlaceholderReflectionHelper.GetAllPlaceholders(this)];
    }

    public List<string> GetAllTemplateStrings()
    {
        return [.. PlaceholderReflectionHelper.GetAllTemplateStrings(this)];
    }

    public void ReplaceAll(
        Dictionary<string, string?> replacements,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        PlaceholderReflectionHelper.ReplaceAll(this, false, replacements, stringComparison);
    }

    /// <summary>
    /// Rewrites every occurrence of the <c>{oldName}</c> token to <c>{newName}</c> across all
    /// templated strings (double-braced occurrences keep their extra brace pair). Used when a
    /// workflow-level signature field is renamed so block templates follow the definition.
    /// </summary>
    public void RewriteToken(
        string oldName,
        string newName,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        var replacement = new Dictionary<string, string?>
        {
            ["{" + oldName + "}"] = "{" + newName + "}",
        };
        PlaceholderReflectionHelper.ReplaceAll(this, false, replacement, stringComparison);
    }

    /// <summary>
    /// Rewrites the templates the placeholder walk cannot see because they render under the
    /// double-brace agent-template grammar. Loop and parameterized expansion call it beside the walk,
    /// so an expanded block carries its own iteration's tags everywhere. Most blocks have none.
    /// </summary>
    public virtual void TransformAgentTemplateTags(Func<string, string> transform)
    {
    }

    public abstract void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison);

    public abstract void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison);

    public abstract void ClearExecutionState();
}
