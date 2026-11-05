using ErrorOr;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Errors;

/// <summary>
/// Provides reusable errors for workflow-execution-related operations.
/// </summary>
public static class WorkflowExecutionErrorFactory
{
    public const string NOT_FOUND_CODE = "WorkflowExecution.NotFound";
    public const string NOT_CANCELLABLE_CODE = "WorkflowExecution.NotCancellable";

    public static Error NotFound()
    {
        return Error.NotFound(NOT_FOUND_CODE, "Workflow execution not found.");
    }

    public static Error BaselineRegistrationConflict()
    {
        return Error.Conflict(
            "WorkflowBaselineRegistration.Conflict",
            $"Another baseline for this workflow type and {CounterpartyTerms.SINGULAR_LOWER} was marked at the same time. Mark the run as a baseline again.");
    }

    public static Error NotCancellable(string currentState)
    {
        return Error.Validation(
            NOT_CANCELLABLE_CODE,
            $"Workflow execution in state '{currentState}' cannot be cancelled.");
    }
}
