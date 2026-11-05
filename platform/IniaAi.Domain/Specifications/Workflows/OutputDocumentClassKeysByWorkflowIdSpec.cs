using Ardalis.Specification;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Domain.Specifications.Workflows;

/// <summary>
/// The output-document class keys of one workflow row — for an executed clone, the classes the
/// run's produced documents were filed under, so a consumer can tell inputs from outputs.
/// </summary>
public sealed class OutputDocumentClassKeysByWorkflowIdSpec : Specification<DocumentClass, string>
{
    public OutputDocumentClassKeysByWorkflowIdSpec(string workflowId)
    {
        Query
            .Where(dc => dc.WorkflowId == workflowId && dc.IsOutputDocument)
            .AsNoTracking()
            .Select(dc => dc.Key);
    }
}
