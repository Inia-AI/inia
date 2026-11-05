using ErrorOr;
using IniaAi.Common.Constants;

namespace IniaAi.Application.Errors;

/// <summary>
/// The errors the freeze submission option returns when the freeze source cannot be replayed into
/// the target submission. Every mismatch blocks: a frozen execution built on different inputs would
/// be a misleading test artifact.
/// </summary>
public static class ExecutionFreezeErrorFactory
{
    public const string SOURCE_NOT_FOUND_CODE = "WorkflowExecution.FreezeSourceNotFound";
    public const string SOURCE_NOT_COMPLETED_CODE = "WorkflowExecution.FreezeSourceNotCompleted";
    public const string WORKFLOW_TYPE_MISMATCH_CODE = "WorkflowExecution.FreezeSourceWorkflowTypeMismatch";
    public const string COUNTERPARTY_MISMATCH_CODE = "WorkflowExecution.FreezeSourceCounterpartyMismatch";
    public const string VARIABLES_MISMATCH_CODE = "WorkflowExecution.FreezeSourceVariablesMismatch";
    public const string VARIABLES_METADATA_KEY = "variables";

    public static Error SourceNotFound(string freezeSourceExecutionId)
    {
        return Error.NotFound(SOURCE_NOT_FOUND_CODE, $"Freeze source execution '{freezeSourceExecutionId}' was not found.");
    }

    public static Error SourceNotCompleted(string freezeSourceExecutionId)
    {
        return Error.Validation(SOURCE_NOT_COMPLETED_CODE, $"Freeze source execution '{freezeSourceExecutionId}' has not completed, so it has no external-fetch outputs to replay.");
    }

    public static Error WorkflowTypeMismatch(string? sourceWorkflowTypeId, string? targetWorkflowTypeId)
    {
        return Error.Validation(WORKFLOW_TYPE_MISMATCH_CODE, $"The freeze source ran workflow type '{sourceWorkflowTypeId}' but this submission targets '{targetWorkflowTypeId}'.");
    }

    public static Error CounterpartyMismatch(string? sourceCounterpartyId, string? targetCounterpartyId)
    {
        return Error.Validation(COUNTERPARTY_MISMATCH_CODE, $"The freeze source ran for {CounterpartyTerms.SINGULAR_LOWER} '{sourceCounterpartyId ?? "(none)"}' but this submission targets '{targetCounterpartyId ?? "(none)"}'.");
    }

    public static Error VariablesMismatch(IReadOnlyList<string> differingTags)
    {
        return Error.Validation(
            VARIABLES_MISMATCH_CODE,
            $"The freeze source ran with different variable values for: {string.Join(", ", differingTags)}.",
            new Dictionary<string, object> { [VARIABLES_METADATA_KEY] = differingTags });
    }
}
