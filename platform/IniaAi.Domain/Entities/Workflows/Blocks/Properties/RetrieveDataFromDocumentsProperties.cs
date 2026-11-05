using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Properties for retrieving data from documents using RAG.
/// Inherits RAG capabilities for document retrieval.
/// </summary>
public class RetrieveDataFromDocumentsProperties : RagBlockProperties
{
    /// <summary>
    /// Whether to retrieve overviews instead of full text from documents.
    /// </summary>
    [JsonProperty(nameof(ShouldUseOverviews))]
    public bool ShouldUseOverviews { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public RetrieveDataFromDocumentsPropertiesExecutionState? ExecutionState { get; set; }

    public RetrieveDataFromDocumentsProperties()
    {
        Type = BlockTypes.SimpleRag;
    }

    public override BlockProperties Clone()
    {
        var clone = new RetrieveDataFromDocumentsProperties();
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new RetrieveDataFromDocumentsPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new RetrieveDataFromDocumentsPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
