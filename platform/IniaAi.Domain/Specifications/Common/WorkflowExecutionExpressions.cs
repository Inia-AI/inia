using IniaAi.Domain.Entities.WorkflowExecutions;
using System.Linq.Expressions;

namespace IniaAi.Domain.Specifications.Common;

/// <summary>
/// Reusable expression library for WorkflowExecution entity filtering.
/// Provides common workflow execution query predicates that can be reused across specifications.
/// </summary>
public static class WorkflowExecutionExpressions
{
    /// <summary>
    /// Expression for filtering by organization ID.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> ByOrganization(string organizationId)
    {
        return we => we.OrganizationId == organizationId;
    }

    /// <summary>
    /// Expression for filtering by workflow execution ID.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> ById(string id)
    {
        return we => we.Id == id;
    }

    /// <summary>
    /// Expression for filtering active (non-deleted) workflow executions.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> IsActive =>
        we => !we.IsDeleted;

    /// <summary>
    /// Expression form of <see cref="WorkflowExecution.IsActive()"/>: the execution has
    /// not yet reached a terminal state. Reads <see cref="WorkflowExecution.ActiveStates"/>
    /// (translated to a SQL IN) so atomic conditional updates re-check the same set the
    /// in-memory guard uses.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> IsInActiveState =>
        we => WorkflowExecution.ActiveStates.Contains(we.WorkflowState);

    /// <summary>
    /// Expression for filtering by workflow state.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> ByWorkflowState(WorkflowState state)
    {
        return we => we.WorkflowState == state;
    }

    /// <summary>
    /// Expression for filtering by multiple workflow states.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> ByWorkflowStates(params WorkflowState[] states)
    {
        return we => states.Contains(we.WorkflowState);
    }

    /// <summary>
    /// Expression for filtering pending workflow executions (running and draft states).
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> IsPending =>
        we => we.WorkflowState == WorkflowState.Running || we.WorkflowState == WorkflowState.Draft;

    /// <summary>
    /// Expression for filtering running workflow executions.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> IsRunning =>
        we => we.WorkflowState == WorkflowState.Running;

    /// <summary>
    /// Expression for filtering confirmed workflow executions.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> IsConfirmed =>
        we => we.WorkflowState == WorkflowState.Confirmed;

    /// <summary>
    /// Expression for filtering by counterparty ID.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> ByCounterparty(string counterpartyId)
    {
        return we => we.CounterpartyId == counterpartyId;
    }

    /// <summary>
    /// Expression for filtering by user ID.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> ByUser(string userId)
    {
        return we => we.UserId == userId;
    }

    /// <summary>
    /// Expression for filtering by workflow ID.
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> ByWorkflow(string workflowId)
    {
        return we => we.WorkflowId == workflowId;
    }

    /// <summary>
    /// Expression for filtering by creation date range (from date).
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> CreatedAfter(DateTime fromDate)
    {
        return we => we.CreatedAt >= fromDate;
    }

    /// <summary>
    /// Expression for filtering by creation date range (to date).
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> CreatedBefore(DateTime toDate)
    {
        return we => we.CreatedAt <= toDate;
    }

    /// <summary>
    /// Expression for filtering recent workflow executions (last N days).
    /// </summary>
    public static Expression<Func<WorkflowExecution, bool>> RecentDays(int days)
    {
        return we => we.CreatedAt >= DateTime.UtcNow.AddDays(-days);
    }
}
