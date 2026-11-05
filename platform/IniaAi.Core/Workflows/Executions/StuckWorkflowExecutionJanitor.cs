using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Workflows.Executions;

/// <summary>
/// One sweep: fails every execution the <see cref="AgedActiveWorkflowExecutionsSpec"/> deems
/// stranded through the guarded terminal funnel — which also settles the agent runs each row
/// recorded — and returns how many rows this sweep marked. Policy and rationale live on
/// <see cref="WorkflowExecutionJanitorSettings"/>.
/// </summary>
public class StuckWorkflowExecutionJanitor
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IWorkflowOutputService _workflowOutputService;
    private readonly WorkflowExecutionJanitorSettings _settings;
    private readonly ILogger<StuckWorkflowExecutionJanitor> _logger;

    public StuckWorkflowExecutionJanitor(
        IUserDataUnitOfWork userDataUnitOfWork,
        IWorkflowOutputService workflowOutputService,
        IOptions<WorkflowExecutionJanitorSettings> settings,
        ILogger<StuckWorkflowExecutionJanitor> logger)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _workflowOutputService = workflowOutputService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        DateTime cutoff = DateTime.UtcNow.AddHours(-_settings.MaxLifetimeHours);
        IList<WorkflowExecution> aged = await _userDataUnitOfWork.WorkflowExecutions
            .GetAllAsync(new AgedActiveWorkflowExecutionsSpec(cutoff), cancellationToken);

        int marked = 0;
        foreach (WorkflowExecution execution in aged)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTime activeSince = execution.StartedAt ?? execution.CreatedAt;
            bool won = await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(
                execution.Id,
                execution.OrganizationId,
                WorkflowExecutionFailureKinds.LIFETIME_EXCEEDED,
                $"Active since {activeSince:O}, past the {_settings.MaxLifetimeHours} h maximum "
                    + "lifetime; the host pumping it is presumed dead");
            if (won)
            {
                marked++;
                _logger.LogWarning(
                    "Janitor failed stuck execution {ExecutionId} ({State} since {ActiveSince})",
                    execution.Id,
                    execution.WorkflowState,
                    activeSince);
            }
        }

        return marked;
    }
}
