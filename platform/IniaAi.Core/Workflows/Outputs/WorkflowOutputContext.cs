using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Core.Workflows.Outputs;

public class WorkflowOutputContext
{
    public WorkflowOutputContext()
    {
    }

    /// <summary>
    /// Shallow copy. Used to snapshot a per-section context during parallel execution so a section's
    /// blocks always write to that section's report section, even after the shared base context is
    /// re-pointed at the next section.
    /// </summary>
    public WorkflowOutputContext(WorkflowOutputContext source)
    {
        CurrentSectionId = source.CurrentSectionId;
        CurrentOrganizationId = source.CurrentOrganizationId;
        CurrentUserId = source.CurrentUserId;
        CurrentCounterparty = source.CurrentCounterparty;
        CurrentPeerCompanies = source.CurrentPeerCompanies;
        CurrentWorkflowOutputRoot = source.CurrentWorkflowOutputRoot;
        RootElementId = source.RootElementId;
        CurrentWorkflowReport = source.CurrentWorkflowReport;
        CurrentReportSection = source.CurrentReportSection;
    }

    public string? CurrentSectionId { get; set; }
    public string? CurrentOrganizationId { get; set; }
    public string? CurrentUserId { get; set; }
    public Counterparty? CurrentCounterparty { get; set; }
    public List<Counterparty>? CurrentPeerCompanies { get; set; }
    public WorkflowExecution? CurrentWorkflowOutputRoot { get; set; }

    public string? RootElementId { get; set; }

    public WorkflowReport? CurrentWorkflowReport { get; set; }

    public ReportSection? CurrentReportSection { get; set; }
}
