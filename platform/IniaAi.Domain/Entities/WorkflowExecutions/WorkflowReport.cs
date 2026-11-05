using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Workflows;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Root of the report tree produced by executing a workflow. Owns the ordered
/// <see cref="ReportSection"/> nodes that make up the report.
/// </summary>
public class WorkflowReport : Entity<WorkflowReport>
{
    public string? WorkflowId { get; set; }
    public string? WorkflowTypeId { get; set; }
    public string? CounterpartyId { get; set; }
    public string? OrganizationId { get; set; }
    public string? UserId { get; set; }
    public string? JobId { get; set; }

    public string? AnalystName { get; set; }
    public DateTime? ReviewDate { get; set; }
    public string? Title { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkflowState WorkflowState { get; set; } = WorkflowState.Undefined;

    public string? WorkflowExecutionId { get; set; }

    [ForeignKey(nameof(WorkflowExecutionId))]
    public WorkflowExecution? WorkflowExecution { get; set; }

    [ForeignKey(nameof(CounterpartyId))]
    public Counterparty? Counterparty { get; set; }

    [InverseProperty(nameof(ReportSection.WorkflowReport))]
    public ICollection<ReportSection> Sections { get; private set; } = [];

    [InverseProperty(nameof(ReportDataOutput.WorkflowReport))]
    public ICollection<ReportDataOutput> DataOutputs { get; private set; } = [];

    [NotMapped]
    public WorkflowType? WorkflowType { get; set; }

    [NotMapped]
    public string? CounterpartyName { get; set; }

    [NotMapped]
    public string? WorkflowTypeName { get; set; }

    public void Add(ReportSection section)
    {
        section.Order = Sections.Count;
        section.WorkflowReport = this;
        section.WorkflowReportId = Id;
        Sections.Add(section);
    }

    public void Remove(ReportSection section)
    {
        if (!Sections.Remove(section))
        {
            return;
        }

        var ordered = Sections.OrderBy(s => s.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }
    }

    public void Clear()
    {
        Sections.Clear();
    }
}
