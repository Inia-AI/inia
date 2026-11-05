using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using System.Linq.Expressions;
using ContractWorkflowState = IniaAi.WebApi.Contracts.Enums.WorkflowState;

namespace IniaAi.Application.Queries.WorkflowExecutions;

public static class WorkflowExecutionProjections
{
    public static readonly Expression<Func<WorkflowExecution, WorkflowExecutionSummaryDto>> ToSummary = execution => new WorkflowExecutionSummaryDto
    {
        Id = execution.Id,
        WorkflowId = execution.WorkflowId,
        CreatedAt = execution.CreatedAt,
        WorkflowState = (ContractWorkflowState)execution.WorkflowState,
        FailureKind = execution.FailureKind,
        FailureDetail = execution.FailureDetail,
        WarningKind = execution.WarningKind,
        WarningDetail = execution.WarningDetail,
        WorkflowTypeId = execution.WorkflowTypeId,
        CounterpartyId = execution.CounterpartyId,
        CounterpartyName = execution.Counterparty != null ? execution.Counterparty.Name : null,
        AnalystName = execution.AnalystName,
        ReviewDate = execution.ReviewDate,
        UserId = execution.UserId,
        Title = execution.Title,
        JobId = execution.JobId,
        IsBaseline = execution.IsBaseline,
        FreezeSourceExecutionId = execution.FreezeSourceExecutionId,
        FreezeReplayedCount = execution.FreezeReplayedCount,
        FreezeEligibleCount = execution.FreezeEligibleCount,
        StartedAt = execution.StartedAt,
        CompletedAt = execution.CompletedAt,
        Duration = execution.CompletedAt - execution.StartedAt,
    };

    public static readonly Expression<Func<WorkflowExecution, WorkflowExecutionDto>> ToDto = execution => new WorkflowExecutionDto
    {
        Id = execution.Id,
        WorkflowId = execution.WorkflowId,
        CreatedAt = execution.CreatedAt,
        WorkflowState = (ContractWorkflowState)execution.WorkflowState,
        FailureKind = execution.FailureKind,
        FailureDetail = execution.FailureDetail,
        WorkflowTypeId = execution.WorkflowTypeId,
        CounterpartyId = execution.CounterpartyId,
        CounterpartyName = execution.Counterparty != null ? execution.Counterparty.Name : null,
        AnalystName = execution.AnalystName,
        ReviewDate = execution.ReviewDate,
        UserId = execution.UserId,
        Title = execution.Title,
        Variables = ExecutionVariableJson.From(execution.Variables),
        JobId = execution.JobId,
        IsBaseline = execution.IsBaseline,
        FreezeSourceExecutionId = execution.FreezeSourceExecutionId,
        FreezeSourceCaseId = execution.FreezeSourceCaseId,
        FreezeReplayedCount = execution.FreezeReplayedCount,
        FreezeEligibleCount = execution.FreezeEligibleCount,
        StartedAt = execution.StartedAt,
        CompletedAt = execution.CompletedAt,
        Duration = execution.CompletedAt - execution.StartedAt,
    };

    private static readonly Func<WorkflowExecution, WorkflowExecutionDto> _toDto = ToDto.Compile();

    public static WorkflowExecutionDto DtoOf(WorkflowExecution execution)
    {
        return _toDto(execution);
    }
}
