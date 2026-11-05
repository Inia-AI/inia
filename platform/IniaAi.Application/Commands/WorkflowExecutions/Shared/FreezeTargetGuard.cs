using ErrorOr;
using IniaAi.Application.Errors;
using IniaAi.Domain.Entities.Workflows;
using System.Text.Json;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// The checks every freeze source passes before its captures replay into a submission: the same
/// workflow type and counterparty, then the same variable values. Every mismatch blocks.
/// </summary>
public static class FreezeTargetGuard
{
    public static Error? TypeAndCounterparty(
        string? sourceWorkflowTypeId,
        string? sourceCounterpartyId,
        Workflow targetWorkflow,
        string? targetCounterpartyId)
    {
        if (!string.Equals(sourceWorkflowTypeId, targetWorkflow.WorkflowTypeId, StringComparison.Ordinal))
        {
            return ExecutionFreezeErrorFactory.WorkflowTypeMismatch(sourceWorkflowTypeId, targetWorkflow.WorkflowTypeId);
        }

        if (!string.Equals(sourceCounterpartyId, targetCounterpartyId, StringComparison.Ordinal))
        {
            return ExecutionFreezeErrorFactory.CounterpartyMismatch(sourceCounterpartyId, targetCounterpartyId);
        }

        return null;
    }

    public static Error? Variables(
        WorkflowVariableFingerprint sourceFingerprint,
        Workflow targetWorkflow,
        Dictionary<string, JsonElement>? targetVariables)
    {
        WorkflowVariableFingerprint targetFingerprint = WorkflowVariableFingerprint.Compute(
            targetWorkflow.Variables, targetVariables);
        IReadOnlyList<string> differingTags = sourceFingerprint.DifferencesFrom(targetFingerprint);

        return differingTags.Count > 0 ? ExecutionFreezeErrorFactory.VariablesMismatch(differingTags) : null;
    }
}
