using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Core.Documents;

/// <summary>
/// A run that found one of its documents deleted when it started: a delete and a submission that
/// arrive together can both go through, and the job then fails the run before it reads anything.
/// </summary>
public sealed class RunInputDeletedException(IReadOnlyList<string> fileNames) : Exception(DetailFor(fileNames)), IWorkflowExecutionFailure
{
    public string FailureKind => WorkflowExecutionFailureKinds.INPUT_DOCUMENT_DELETED;

    public string FailureDetail => Message;

    private static string DetailFor(IReadOnlyList<string> fileNames)
    {
        string subject = fileNames.Count == 1
            ? $"The document {fileNames[0]} was deleted"
            : $"The documents {string.Join(", ", fileNames)} were deleted";

        return $"{subject} before the report started, so the report did not run. Start the report again with the documents it needs.";
    }
}
