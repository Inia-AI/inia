using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>
/// The current template of a workflow type with only what a run's inputs are checked against:
/// its variables and its document classes. Sections and blocks stay unloaded. Resolves the same
/// row as <c>CoreWorkflowRepository.GetByTypeIdAsync</c> (newest by <c>CreatedAt</c>).
/// </summary>
public sealed class WorkflowTemplateInputsByTypeSpec : Specification<Workflow>, ISingleResultSpecification<Workflow>
{
    public WorkflowTemplateInputsByTypeSpec(string workflowTypeId)
    {
        _ = Query
            .Where(w => w.WorkflowTypeId == workflowTypeId)
            .OrderByDescending(w => w.CreatedAt)
            .Include(w => w.Variables)
            .Include(w => w.DocumentClasses)
            .AsNoTracking();
    }
}
