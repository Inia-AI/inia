using System.Text.Json;
using ErrorOr;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Replay;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// Turns the freeze submission option ("replay external-fetch outputs from execution B") into the
/// replay set captured from that execution, guarding that the target submission matches it on
/// workflow type, counterparty and variable fingerprint.
/// </summary>
public interface IExecutionFreezeSourceResolver
{
    Task<ErrorOr<IReadOnlyList<ReplayOutput>>> ResolveAsync(
        string organizationId,
        string freezeSourceExecutionId,
        Workflow targetWorkflow,
        string? targetCounterpartyId,
        Dictionary<string, JsonElement>? targetVariables,
        CancellationToken cancellationToken);
}
