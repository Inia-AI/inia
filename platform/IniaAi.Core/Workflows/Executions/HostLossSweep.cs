using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IniaAi.Core.Workflows.Executions;

/// <summary>
/// One sweep: fails, as <see cref="WorkflowExecutionFailureKinds.HOST_LOST"/>, every active execution
/// held by a process start that missed four heartbeats, through the guarded terminal funnel, and
/// returns how many it marked. A run of this process, a run with no recorded start, and a run whose
/// start never wrote its row are left to the janitor, as is a start not seen for longer than the
/// janitor's maximum lifetime, whose runs the janitor has already failed. It judges other starts
/// only once this process's own heartbeat has succeeded for <see cref="HEALTHY_BEFORE_JUDGING"/>
/// without a break: after a database outage stopped every heartbeat, each live start has beaten
/// again by then, so the outage does not read as every other host lost.
/// </summary>
public class HostLossSweep
{
    public static readonly TimeSpan HEARTBEAT_INTERVAL = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan LOST_AFTER = HEARTBEAT_INTERVAL * 4;

    public static readonly TimeSpan HEALTHY_BEFORE_JUDGING = HEARTBEAT_INTERVAL * 2;

    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IWorkflowOutputService _workflowOutputService;
    private readonly HostBootIdentity _hostBoot;
    private readonly WorkflowExecutionJanitorSettings _settings;
    private readonly ILogger<HostLossSweep> _logger;
    private readonly TimeProvider _timeProvider;

    public HostLossSweep(
        IUserDataUnitOfWork userDataUnitOfWork,
        IWorkflowOutputService workflowOutputService,
        HostBootIdentity hostBoot,
        IOptions<WorkflowExecutionJanitorSettings> settings,
        ILogger<HostLossSweep> logger,
        TimeProvider? timeProvider = null)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _workflowOutputService = workflowOutputService;
        _hostBoot = hostBoot;
        _settings = settings.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset clock = _timeProvider.GetUtcNow();
        if (_hostBoot.HeartbeatHealthySince is not DateTimeOffset healthySince || clock - healthySince < HEALTHY_BEFORE_JUDGING)
        {
            return 0;
        }

        DateTime now = clock.UtcDateTime;
        Dictionary<string, HostBoot> lostBoots = (await _userDataUnitOfWork.Repository<HostBoot>()
                .GetAllAsync(
                    new LostHostBootsSpec(now.AddHours(-_settings.MaxLifetimeHours), now - LOST_AFTER, _hostBoot.Id),
                    cancellationToken))
            .ToDictionary(boot => boot.Id);
        if (lostBoots.Count == 0)
        {
            return 0;
        }

        IList<WorkflowExecution> stranded = await _userDataUnitOfWork.WorkflowExecutions
            .GetAllAsync(new ActiveWorkflowExecutionsOnHostsSpec(lostBoots.Keys), cancellationToken);

        int marked = 0;
        foreach (WorkflowExecution execution in stranded)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HostBoot boot = lostBoots[execution.HostBootId!];
            bool won = await _workflowOutputService.MarkExecutionAsFailedIfActiveAsync(
                execution.Id,
                execution.OrganizationId,
                WorkflowExecutionFailureKinds.HOST_LOST,
                $"The server running this report stopped before the report finished (last seen {boot.LastSeenAt:u}). Run the report again.");
            if (won)
            {
                marked++;
                _logger.LogWarning(
                    "Host-loss sweep failed execution {ExecutionId} ({State}): its host {HostBootId} on {Machine} was last seen at {LastSeenAt:O}",
                    execution.Id,
                    execution.WorkflowState,
                    boot.Id,
                    boot.Machine,
                    boot.LastSeenAt);
            }
        }

        return marked;
    }
}
