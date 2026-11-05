using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Errors;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.Runs;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Application.Commands.WorkflowExecutions.UpdateWorkflowExecutionState;

public class UpdateWorkflowExecutionStateCommandHandler
    : IRequestHandler<UpdateWorkflowExecutionStateCommand, ErrorOr<Success>>
{
    private readonly IUserDataUnitOfWork _unitOfWork;
    private readonly IJobsService _jobsService;
    private readonly IAgentRunTerminalCleanup? _agentRunTerminalCleanup;
    private readonly IRunSettlement _runSettlement;

    public UpdateWorkflowExecutionStateCommandHandler(
        IUserDataUnitOfWork unitOfWork,
        IJobsService jobsService,
        IAgentRunTerminalCleanup? agentRunTerminalCleanup = null,
        IRunSettlement? runSettlement = null)
    {
        _unitOfWork = unitOfWork;
        _jobsService = jobsService;
        _agentRunTerminalCleanup = agentRunTerminalCleanup;
        _runSettlement = runSettlement ?? new NoRunSettlement();
    }

    public async Task<ErrorOr<Success>> Handle(
        UpdateWorkflowExecutionStateCommand request,
        CancellationToken cancellationToken)
    {
        var spec = new WorkflowExecutionByIdTrackedSpec(request.WorkflowExecutionId, request.OrganizationId);
        WorkflowExecution? execution = await _unitOfWork.Repository<WorkflowExecution>()
            .GetFirstOrDefaultAsync(spec, cancellationToken);

        if (execution is null)
        {
            return WorkflowExecutionErrorFactory.NotFound();
        }

        if (!execution.IsActive())
        {
            return WorkflowExecutionErrorFactory.NotCancellable(execution.WorkflowState.ToString());
        }

        if (!string.IsNullOrEmpty(execution.JobId))
        {
            _jobsService.CancelJob(execution.JobId);
        }

        bool won = await _unitOfWork.WorkflowExecutions.TryMarkTerminalIfActiveAsync(
            request.WorkflowExecutionId,
            request.OrganizationId,
            (WorkflowState)request.TargetState,
            cancellationToken: cancellationToken);

        if (!won)
        {
            return WorkflowExecutionErrorFactory.NotCancellable(
                "execution moved to a terminal state during cancellation");
        }

        if (_agentRunTerminalCleanup is not null
            && (WorkflowState)request.TargetState == WorkflowState.Cancelled)
        {
            try
            {
                await _agentRunTerminalCleanup.CleanUpAsync(
                    request.WorkflowExecutionId, request.OrganizationId, WorkflowState.Cancelled);
            }
            catch (Exception)
            {
                // The cancel itself won; a cleanup fault must not turn a done cancel into a 500.
            }
        }

        try
        {
            await _runSettlement.SettleAsync(request.WorkflowExecutionId, request.OrganizationId);
        }
        catch (Exception)
        {
            // A settlement fault must not turn a done cancel into a 500 either.
        }

        return Result.Success;
    }
}
