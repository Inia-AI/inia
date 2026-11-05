using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Specifications.Common;
using IniaAi.Infrastructure.Persistence.Abstractions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Persistence.UserData.Repositories;

public class WorkflowExecutionRepository : RepositoryBase<WorkflowExecution>, IWorkflowExecutionRepository
{
    public WorkflowExecutionRepository(UserDataDbContext context) : base(context)
    {
    }

    public async Task<bool> TryMarkTerminalIfActiveAsync(
        string workflowExecutionId,
        string organizationId,
        WorkflowState terminalState,
        string? failureKind = null,
        string? failureDetail = null,
        CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.UtcNow;
        string? kind = Truncate(failureKind, WorkflowExecution.FAILURE_KIND_MAX_LENGTH);
        string? detail = Truncate(failureDetail, WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH);

        if (!Context.Database.IsRelational())
        {
            WorkflowExecution? execution = await ForExecution(workflowExecutionId, organizationId)
                .FirstOrDefaultAsync(cancellationToken);

            if (execution is null || !execution.IsActive())
            {
                return false;
            }

            execution.WorkflowState = terminalState;

            if (terminalState is WorkflowState.Failed)
            {
                execution.StartedAt ??= now;
                execution.FailureKind = kind;
                execution.FailureDetail = detail;
            }

            execution.CompletedAt ??= now;
            _ = await Context.SaveChangesAsync(cancellationToken);
            return true;
        }

        IQueryable<WorkflowExecution> activeExecution = ForExecution(workflowExecutionId, organizationId)
            .Where(WorkflowExecutionExpressions.IsInActiveState);

        int rowsUpdated = terminalState is WorkflowState.Failed
            ? await activeExecution.ExecuteUpdateAsync(
                s => s.SetProperty(e => e.WorkflowState, terminalState)
                    .SetProperty(e => e.StartedAt, e => e.StartedAt ?? now)
                    .SetProperty(e => e.CompletedAt, e => e.CompletedAt ?? now)
                    .SetProperty(e => e.FailureKind, kind)
                    .SetProperty(e => e.FailureDetail, detail),
                cancellationToken)
            : await activeExecution.ExecuteUpdateAsync(
                s => s.SetProperty(e => e.WorkflowState, terminalState)
                    .SetProperty(e => e.CompletedAt, e => e.CompletedAt ?? now),
                cancellationToken);

        return rowsUpdated > 0;
    }

    public async Task<bool> TryTransitionStateIfActiveAsync(
        WorkflowExecution execution,
        WorkflowState state,
        string? warningKind = null,
        string? warningDetail = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        DateTime now = DateTime.UtcNow;
        DateTime? startedAt = execution.StartedAt ?? (state is WorkflowState.Queued ? null : now);
        DateTime? completedAt = execution.CompletedAt ?? (WorkflowExecution.IsActiveState(state) ? null : now);
        string? warning = Truncate(warningKind, WorkflowExecution.FAILURE_KIND_MAX_LENGTH);
        string? warningText = Truncate(warningDetail, WorkflowExecution.FAILURE_DETAIL_MAX_LENGTH);

        if (!Context.Database.IsRelational())
        {
            // Against the stored row, not the caller's tracked instance: the whole point of the
            // guard is that someone else may have moved the row since this context loaded it.
            bool active = await ForExecution(execution.Id, execution.OrganizationId)
                .AsNoTracking()
                .Where(WorkflowExecutionExpressions.IsInActiveState)
                .AnyAsync(cancellationToken);

            if (!active)
            {
                return false;
            }

            WorkflowExecution? tracked = await ForExecution(execution.Id, execution.OrganizationId)
                .FirstOrDefaultAsync(cancellationToken);

            if (tracked is null)
            {
                return false;
            }

            tracked.WorkflowState = state;
            tracked.StartedAt = tracked.StartedAt ?? startedAt;
            tracked.CompletedAt = tracked.CompletedAt ?? completedAt;

            if (warning is not null)
            {
                tracked.WarningKind = warning;
                tracked.WarningDetail = warningText;
            }

            _ = await Context.SaveChangesAsync(cancellationToken);

            ApplyToCaller(execution, state, tracked.StartedAt, tracked.CompletedAt);

            if (warning is not null)
            {
                execution.WarningKind = tracked.WarningKind;
                execution.WarningDetail = tracked.WarningDetail;
            }

            return true;
        }

        int rowsUpdated = await ForExecution(execution.Id, execution.OrganizationId)
            .Where(WorkflowExecutionExpressions.IsInActiveState)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.WorkflowState, state)
                    .SetProperty(e => e.StartedAt, e => e.StartedAt ?? startedAt)
                    .SetProperty(e => e.CompletedAt, e => e.CompletedAt ?? completedAt)
                    .SetProperty(e => e.WarningKind, e => warning ?? e.WarningKind)
                    .SetProperty(e => e.WarningDetail, e => warning == null ? e.WarningDetail : warningText),
                cancellationToken);

        if (rowsUpdated == 0)
        {
            return false;
        }

        // The UPDATE's coalescing may have kept the row's own timestamps (stamped by another
        // writer this instance never saw), so mirror what is actually stored.
        var stored = await ForExecution(execution.Id, execution.OrganizationId)
            .AsNoTracking()
            .Select(e => new { e.StartedAt, e.CompletedAt })
            .FirstOrDefaultAsync(cancellationToken);

        ApplyToCaller(execution, state, stored?.StartedAt ?? startedAt, stored?.CompletedAt ?? completedAt);

        if (warning is not null)
        {
            execution.WarningKind = warning;
            execution.WarningDetail = warningText;
        }

        return true;
    }

    public async Task<WorkflowState?> GetStoredStateAsync(
        string workflowExecutionId,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        return await ForExecution(workflowExecutionId, organizationId)
            .AsNoTracking()
            .Select(e => (WorkflowState?)e.WorkflowState)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateReplayOutcomeAsync(
        string workflowExecutionId,
        string? organizationId,
        string? replayUnmatchedJson,
        int freezeReplayedCount,
        int freezeEligibleCount,
        CancellationToken cancellationToken = default)
    {
        if (!Context.Database.IsRelational())
        {
            WorkflowExecution? tracked = await ForExecution(workflowExecutionId, organizationId)
                .FirstOrDefaultAsync(cancellationToken);

            if (tracked is null)
            {
                return;
            }

            tracked.ReplayUnmatchedJson = replayUnmatchedJson;
            tracked.FreezeReplayedCount = freezeReplayedCount;
            tracked.FreezeEligibleCount = freezeEligibleCount;
            _ = await Context.SaveChangesAsync(cancellationToken);
            return;
        }

        _ = await ForExecution(workflowExecutionId, organizationId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.ReplayUnmatchedJson, replayUnmatchedJson)
                    .SetProperty(e => e.FreezeReplayedCount, freezeReplayedCount)
                    .SetProperty(e => e.FreezeEligibleCount, freezeEligibleCount),
                cancellationToken);
    }

    public async Task UpdateCounterpartyIdAsync(
        string workflowExecutionId,
        string? organizationId,
        string? counterpartyId,
        CancellationToken cancellationToken = default)
    {
        if (!Context.Database.IsRelational())
        {
            WorkflowExecution? tracked = await ForExecution(workflowExecutionId, organizationId)
                .FirstOrDefaultAsync(cancellationToken);

            if (tracked is null)
            {
                return;
            }

            tracked.CounterpartyId = counterpartyId;
            _ = await Context.SaveChangesAsync(cancellationToken);
            return;
        }

        _ = await ForExecution(workflowExecutionId, organizationId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.CounterpartyId, counterpartyId),
                cancellationToken);
    }

    private IQueryable<WorkflowExecution> ForExecution(string workflowExecutionId, string? organizationId)
    {
        return Entities.Where(e => e.Id == workflowExecutionId
            && e.OrganizationId == organizationId
            && !e.IsDeleted);
    }

    /// <summary>
    /// Brings the caller's instance in line with the row the conditional update just wrote and
    /// leaves it Unchanged, so the next <c>SaveChanges</c> on this context cannot follow up with a
    /// full-entity write that would undo the guard.
    /// </summary>
    private void ApplyToCaller(
        WorkflowExecution execution,
        WorkflowState state,
        DateTime? startedAt,
        DateTime? completedAt)
    {
        execution.WorkflowState = state;
        execution.StartedAt = startedAt;
        execution.CompletedAt = completedAt;

        if (Context.Entry(execution).State is not EntityState.Detached)
        {
            Context.Entry(execution).State = EntityState.Unchanged;
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        // Cutting between a surrogate pair's halves produces a string PostgreSQL rejects at
        // encode time — and a rejected UPDATE here is the Failed mark itself going missing.
        int cut = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..cut];
    }
}
