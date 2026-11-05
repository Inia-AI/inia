using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for building topics from documents.
/// Inherits RAG capabilities for document retrieval.
/// </summary>
public class BuildTopicsProperties : RagBlockProperties
{
    /// <summary>
    /// Maximum number of topics to generate.
    /// </summary>
    [JsonProperty(nameof(MaxTopics), NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxTopics { get; set; }

    /// <summary>
    /// Direct use of this property is not recommended as it is meant to be a
    /// template for the topics-model text. It can contain placeholders that
    /// get replaced at execution time. Use
    /// <see cref="BuildTopicsPropertiesExecutionState.TopicsModel"/> for the
    /// actual topics model after replacements.
    /// </summary>
    [JsonProperty(nameof(TopicsModelTemplate), NullValueHandling = NullValueHandling.Ignore)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? TopicsModelTemplate { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public BuildTopicsPropertiesExecutionState? ExecutionState { get; set; }

    public BuildTopicsProperties()
    {
        Type = BlockTypes.BuildTopicsForAllDocuments;
    }

    public override BlockProperties Clone()
    {
        var clone = new BuildTopicsProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new BuildTopicsPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new BuildTopicsPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
