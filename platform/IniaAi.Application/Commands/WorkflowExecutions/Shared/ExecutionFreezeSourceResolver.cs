using System.Text.Json;
using ErrorOr;
using IniaAi.Application.Errors;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Replay;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

public class ExecutionFreezeSourceResolver : IExecutionFreezeSourceResolver
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly ILogger<ExecutionFreezeSourceResolver> _logger;

    public ExecutionFreezeSourceResolver(
        IUserDataUnitOfWork unitOfWork,
        ILogger<ExecutionFreezeSourceResolver> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<ReplayOutput>>> ResolveAsync(
        string organizationId,
        string freezeSourceExecutionId,
        Workflow targetWorkflow,
        string? targetCounterpartyId,
        Dictionary<string, JsonElement>? targetVariables,
        CancellationToken cancellationToken)
    {
        WorkflowExecution? source = await _unitOfWork.Repository<WorkflowExecution>()
            .GetFirstOrDefaultAsync(
                new WorkflowExecutionByIdAndOrganizationSpec(freezeSourceExecutionId, organizationId),
                cancellationToken);

        if (source is null)
        {
            return ExecutionFreezeErrorFactory.SourceNotFound(freezeSourceExecutionId);
        }

        if (!source.HasCompleted())
        {
            return ExecutionFreezeErrorFactory.SourceNotCompleted(freezeSourceExecutionId);
        }

        if (FreezeTargetGuard.TypeAndCounterparty(source.WorkflowTypeId, source.CounterpartyId, targetWorkflow, targetCounterpartyId) is Error identityMismatch)
        {
            return identityMismatch;
        }

        Workflow? clone = await LoadExecutedCloneAsync(source, cancellationToken);

        WorkflowVariableFingerprint sourceFingerprint = WorkflowVariableFingerprint.Compute(
            clone?.Variables ?? targetWorkflow.Variables, StoredVariables(source));

        if (FreezeTargetGuard.Variables(sourceFingerprint, targetWorkflow, targetVariables) is Error variablesMismatch)
        {
            return variablesMismatch;
        }

        return ErrorOrFactory.From(CaptureReplayOutputs(source, clone));
    }

    /// <summary>
    /// The source's executed workflow clone: the template as it stood when the source ran, so the
    /// source's omitted variables read as the defaults it actually ran with, and the block
    /// responses the replay is served from.
    /// </summary>
    private async Task<Workflow?> LoadExecutedCloneAsync(WorkflowExecution source, CancellationToken cancellationToken)
    {
        Workflow? clone = string.IsNullOrEmpty(source.WorkflowId)
            ? null
            : await _unitOfWork.Workflows.GetByIdAsync(source.WorkflowId, cancellationToken);

        if (clone is null)
        {
            _logger.LogWarning(
                "Freeze source execution {ExecutionId} has no loadable workflow clone; its variables read against the current template and every freeze-eligible block will run live",
                source.Id);
        }

        return clone;
    }

    private static Dictionary<string, JsonElement>? StoredVariables(WorkflowExecution source)
    {
        return source.Variables?.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value));
    }

    /// <summary>
    /// The shared capture walk over the source's executed workflow clone. A source whose clone
    /// is no longer loadable yields an empty set, so every eligible block runs live and the
    /// execution discloses zero coverage.
    /// </summary>
    private IReadOnlyList<ReplayOutput> CaptureReplayOutputs(WorkflowExecution source, Workflow? clone)
    {
        if (clone is null)
        {
            return [];
        }

        IReadOnlyList<ReplayOutput> captures = FreezeCaptureWalk.Collect(clone);

        _logger.LogInformation(
            "Captured {Count} external-fetch output(s) from freeze source execution {ExecutionId}",
            captures.Count,
            source.Id);

        return captures;
    }
}
