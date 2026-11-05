using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Application.Queries.ExecutedWorkflows.DownloadExecutedWorkflow;

/// <summary>
/// The downloaded form of an executed run: its workflow with every block's run evidence, and the
/// text of each page its citation traces point at, so the traces read the same away from the
/// database that recorded them.
/// </summary>
public static class ExecutedWorkflowExport
{
    public static async Task<string> ToJsonAsync(
        Workflow workflow,
        IUserDataUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        workflow.NormalizeSectionOrdering();
        foreach (Section section in workflow.Sections)
        {
            section.NormalizeBlockOrdering();
        }

        List<string> pageIds = workflow.CitationTracePageIds();

        if (pageIds.Count > 0 && workflow.OrganizationId is string organizationId)
        {
            workflow.CitationTracePages = [.. await unitOfWork.Repository<Page>()
                .GetAllAsync(new CitationTracePagesSpec(organizationId, pageIds), cancellationToken)];
        }

        return workflow.ToExportJson(includeRunTraces: true);
    }
}
