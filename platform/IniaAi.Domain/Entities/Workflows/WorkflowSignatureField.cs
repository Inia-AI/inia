using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// A workflow-level signature field definition. Blocks reference a definition by carrying the
/// <c>{Name}</c> token in their prompt templates — the token is the binding, nothing is stored on
/// the block — so the definition (and every client override keyed by <see cref="Name"/>) survives
/// block restructuring, and one field can be shared by any number of blocks.
/// </summary>
public class WorkflowSignatureField : JsonSerializable<WorkflowSignatureField>
{
    [Key]
    [JsonIgnore]
    [CloneIgnore]
    public string Id { get; internal set; } = Guid.NewGuid().ToString();

    [JsonIgnore]
    [CloneIgnore]
    public string WorkflowId { get; set; } = string.Empty;

    [JsonIgnore]
    [CloneIgnore]
    public Workflow? Workflow { get; set; }

    /// <summary>
    /// The placeholder name; the identity that override rows are keyed by and the token blocks
    /// reference as <c>{Name}</c>. Unique per workflow (case-insensitive), enforced by save
    /// validation. Max length matches <c>SignatureFieldOverride.PlaceholderName</c> (200).
    /// </summary>
    [StringLength(200)]
    [JsonProperty(nameof(Name))]
    [CloneIgnore]
    public string Name { get; set; } = string.Empty;

    [StringLength(450)]
    [JsonProperty(nameof(DisplayName))]
    public string DisplayName { get; set; } = string.Empty;

    [JsonProperty(nameof(Description))]
    public string Description { get; set; } = string.Empty;

    /// <summary>The staff-authored value injected when no client override row exists.</summary>
    [JsonProperty(nameof(DefaultValue))]
    public string DefaultValue { get; set; } = string.Empty;

    [JsonConstructor]
    internal WorkflowSignatureField()
    {
    }

    public WorkflowSignatureField(Workflow workflow, string name)
    {
        Workflow = workflow;
        WorkflowId = workflow.Id;
        Name = name;
        workflow.SignatureFields.Add(this);
    }

    public WorkflowSignatureField CloneToWorkflow(Workflow workflow)
    {
        var clone = new WorkflowSignatureField(workflow, Name);
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }
}
