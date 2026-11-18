using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.Workflows;

public static class CurrentWorkflowQueries
{
    /// <summary>The newest workflow of each type, one per type, as the run paths pick a type's workflow.</summary>
    public static IQueryable<Workflow> CurrentOfEachType(this IQueryable<Workflow> workflows)
    {
        return workflows.Where(workflow => workflow.WorkflowTypeId != null
            && workflow.Id == workflows
                .Where(other => other.WorkflowTypeId == workflow.WorkflowTypeId)
                .OrderByDescending(other => other.CreatedAt)
                .Select(other => other.Id)
                .First());
    }
}
