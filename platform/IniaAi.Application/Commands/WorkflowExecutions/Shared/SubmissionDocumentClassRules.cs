using IniaAi.Core.Services.DocumentClassValidation;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// Which document class rules refuse a submission. The submission path and the re-run check both
/// read this, so a re-run the check calls ready is one the submission accepts.
/// </summary>
public static class SubmissionDocumentClassRules
{
    /// <summary>
    /// The unsatisfied class statuses that refuse a submission.
    /// </summary>
    public static List<DocumentClassStatus> Refusing(Workflow workflow, DocumentClassValidationResult documentValidation)
    {
        return [.. documentValidation.DocumentClassStatuses.Where(status => !status.IsSatisfied)];
    }
}
