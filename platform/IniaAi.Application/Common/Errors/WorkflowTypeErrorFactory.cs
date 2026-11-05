using ErrorOr;

namespace IniaAi.Application.Errors;

/// <summary>
/// Provides reusable errors for workflow type operations.
/// </summary>
public static class WorkflowTypeErrorFactory
{
    public const string NOT_FOUND_CODE = "WorkflowType.NotFound";
    public const string RESERVED_CODE = "WorkflowType.Reserved";
    public const string SHARED_CODE = "WorkflowType.SharedWithOtherOrganizations";
    public const string RUN_IN_PROGRESS_CODE = "WorkflowType.RunInProgress";
    public const string ALREADY_EXISTS_CODE = "WorkflowType.AlreadyExists";

    public static Error NotFound(string workflowTypeId)
    {
        return Error.NotFound(
            NOT_FOUND_CODE,
            $"The workflow type '{workflowTypeId}' does not exist. Check the id against the list of workflow types.");
    }

    public static Error Deleted(string workflowTypeId)
    {
        return Error.NotFound(
            NOT_FOUND_CODE,
            $"The workflow type '{workflowTypeId}' was deleted, so it cannot change. Save the workflow under a new workflow type id.");
    }

    public static Error AlreadyExists(string workflowTypeId)
    {
        return Error.Conflict(
            ALREADY_EXISTS_CODE,
            $"A workflow type with the id '{workflowTypeId}' already exists. Choose a different id.");
    }

    public static Error IdHeldByDeleted(string workflowTypeId)
    {
        return Error.Conflict(
            ALREADY_EXISTS_CODE,
            $"The id '{workflowTypeId}' belongs to a workflow type that was deleted, and it stays taken until the weekly cleanup removes that type. Choose a different id.");
    }

    public static Error Reserved(string workflowTypeId)
    {
        return Error.Validation(
            RESERVED_CODE,
            $"The workflow type '{workflowTypeId}' belongs to the platform, so it was not deleted.");
    }

    public static Error Shared(string workflowTypeId)
    {
        return Error.Conflict(
            SHARED_CODE,
            $"Another organization also uses the workflow type '{workflowTypeId}', so it was not deleted. Ask Inia support to remove it.");
    }

    public static Error RunInProgress(string workflowTypeId)
    {
        return Error.Conflict(
            RUN_IN_PROGRESS_CODE,
            $"A run of the workflow type '{workflowTypeId}' has not finished, so the type was not deleted. Delete it again once the run has finished.");
    }
}
