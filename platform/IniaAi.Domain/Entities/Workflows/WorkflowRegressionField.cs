using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// A workflow-level regression metric definition (name, type, tolerance). The block that produces
/// the field carries only a binding (source + locator) in <see cref="Blocks.Block.RegressionSchema"/>,
/// keyed by <see cref="Name"/>, so the metric survives block restructuring: deleting a block leaves
/// the definition in place to be re-bound instead of losing it.
/// </summary>
public class WorkflowRegressionField : JsonSerializable<WorkflowRegressionField>
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
    /// The field name; the join key between this definition and a block binding. Unique per workflow
    /// (case-insensitive), enforced by save validation.
    /// </summary>
    [StringLength(450)]
    [JsonProperty(nameof(Name))]
    [CloneIgnore]
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="RegressionFieldTypes"/> ("numeric" or "text").</summary>
    [StringLength(50)]
    [JsonProperty(nameof(Type))]
    public string Type { get; set; } = RegressionFieldTypes.NUMERIC;

    /// <summary>Percentage tolerance for numeric comparisons (e.g. 5 for ±5%). Ignored for text fields.</summary>
    [Precision(28, 8)]
    [JsonProperty(nameof(Tolerance))]
    public decimal Tolerance { get; set; }

    [JsonConstructor]
    internal WorkflowRegressionField()
    {
    }

    public WorkflowRegressionField(Workflow workflow, string name)
    {
        Workflow = workflow;
        WorkflowId = workflow.Id;
        Name = name;
        workflow.RegressionFields.Add(this);
    }

    public WorkflowRegressionField CloneToWorkflow(Workflow workflow)
    {
        var clone = new WorkflowRegressionField(workflow, Name);
        CloneHelper.CopyScalars(this, clone);
        return clone;
    }
}
