using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.WorkflowExecutions;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.AgentRuns;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.AgentRuns.ExecutionAgentRunFiles;

public class ListExecutionAgentRunFilesQueryHandler
    : IRequestHandler<ListExecutionAgentRunFilesQuery, ErrorOr<List<AgentRunFileDto>>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;

    public ListExecutionAgentRunFilesQueryHandler(IUserDataUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ErrorOr<List<AgentRunFileDto>>> Handle(
        ListExecutionAgentRunFilesQuery request,
        CancellationToken cancellationToken)
    {
        ErrorOr<WorkflowExecution> execution = await RunEvidenceAccess.ResolveAsync(
            _unitOfWork,
            request.OrganizationId,
            request.UserId,
            request.ExecutionId,
            request.CallerReadsEveryRun,
            cancellationToken);

        if (execution.IsError)
        {
            return execution.Errors;
        }

        if (string.IsNullOrWhiteSpace(execution.Value.WorkflowId))
        {
            return new List<AgentRunFileDto>();
        }

        return await ExecutionAgentRunFiles
            .OfWorkflow(_unitOfWork.Repository<AgentRunTraceFile>().GetAll().AsNoTracking(), request.OrganizationId, execution.Value.WorkflowId)
            .OrderBy(file => file.Block!.Section!.Order)
            .ThenBy(file => file.Block!.Order)
            .ThenBy(file => file.RunExecutionId)
            .ThenBy(file => file.Attempt)
            .ThenBy(file => file.Path)
            .Select(ExecutionAgentRunFiles.ToDto)
            .ToListAsync(cancellationToken);
    }
}
