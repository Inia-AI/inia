using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Closes an execution's agent-run business at its terminal, in two halves: the session
/// bundles it holds are released on the spot, then every run the ledger records as
/// unacknowledged is settled.
/// Reads only the agent-family blocks of the execution's snapshot workflow (persisted under the
/// execution's own id) — a projected query, never the full block graph and never a jsonb scan
/// across the table — and settles every run recorded as unacknowledged through the executor's
/// shared abandonment teardown, anchored to the run's own block so the collected evidence is
/// committed before the acknowledgement releases the runtime's copy. Works in a fresh scope
/// of its own because callers invoke it from dying request scopes and teardown paths.
/// </summary>
public class AgentRunTerminalCleanup : IAgentRunTerminalCleanup
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IAgentRunSessionBundleReaper? _bundleReaper;
    private readonly ILogger<AgentRunTerminalCleanup> _logger;

    public AgentRunTerminalCleanup(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AgentRunTerminalCleanup> logger,
        IAgentRunSessionBundleReaper? bundleReaper = null)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _bundleReaper = bundleReaper;
        _logger = logger;
    }

    public async Task CleanUpAsync(
        string workflowExecutionId,
        string organizationId,
        WorkflowState terminalState)
    {
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

            var agentFamilyBlocks = await context.Blocks
                .AsNoTracking()
                .Where(block => BlockTypesExtensions.AgentFamily.Contains(block.Type)
                    && block.Section!.WorkflowId == workflowExecutionId
                    && context.WorkflowExecutions.Any(execution =>
                        execution.Id == workflowExecutionId
                        && execution.OrganizationId == organizationId))
                .Select(block => new { block.Id, block.Properties })
                .ToListAsync();

            if (agentFamilyBlocks.Count == 0)
            {
                return;
            }

            await ReleaseSessionBundlesAsync(workflowExecutionId, organizationId, terminalState);

            Dictionary<string, string> blockIdByRunId = [];
            foreach (var familyBlock in agentFamilyBlocks)
            {
                if (familyBlock.Properties is not IAgentRunProperties properties)
                {
                    continue;
                }

                foreach (AgentRunPass pass in (properties.RunLedger ?? []).Where(NeedsSettling))
                {
                    _ = blockIdByRunId.TryAdd(pass.ExecutionId, familyBlock.Id);
                }
            }

            if (blockIdByRunId.Count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "Terminal cleanup for execution {ExecutionId}: settling {Count} unacknowledged run(s)",
                workflowExecutionId,
                blockIdByRunId.Count);

            IAgentRunExecutor executor = scope.ServiceProvider.GetRequiredService<IAgentRunExecutor>();
            await Task.WhenAll(blockIdByRunId.Select(entry => SettleRunAsync(
                executor,
                workflowExecutionId,
                entry.Key,
                new AgentRunTraceAnchor(organizationId, entry.Value))));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Terminal agent-run cleanup failed for execution {ExecutionId}; " +
                "unacknowledged runs stay on the ledger for the runtime's retention reaper",
                workflowExecutionId);
        }
    }

    /// <summary>
    /// Only rows that may still be spending get settled: a null status is the start-time row
    /// of a run nobody watched settle — this ticket's leak — and a non-terminal status is a
    /// run observed alive when its watcher died. A terminal or unreadable status means the
    /// run already settled and its complete was withheld on purpose (failed trace collection,
    /// a state nobody could read); completing it now would purge the evidence pack the
    /// withhold rules preserved, so those rows stay for the runtime's retention reaper.
    /// </summary>
    private static bool NeedsSettling(AgentRunPass pass)
    {
        if (!pass.Unacknowledged || string.IsNullOrEmpty(pass.ExecutionId))
        {
            return false;
        }

        if (pass.Status is null)
        {
            return true;
        }

        return Enum.TryParse(pass.Status, out AgentRunStatus status)
            && status is AgentRunStatus.Running
                or AgentRunStatus.AwaitingInput
                or AgentRunStatus.Cancelling;
    }

    /// <summary>
    /// Closes the execution's bundle custody, ahead of the settling half — settling reaches out
    /// to the runtime and can be slow, and the bundles of an execution that will never redo
    /// anything should not wait on it. Gated on the execution having agent-family blocks at all:
    /// every other execution in the deployment reaches this on its terminal too, and only an
    /// agent-family block can hold a bundle.
    /// </summary>
    private async Task ReleaseSessionBundlesAsync(
        string workflowExecutionId,
        string organizationId,
        WorkflowState terminalState)
    {
        if (_bundleReaper is null)
        {
            return;
        }

        AgentRunSessionBundleRelease? release = terminalState switch
        {
            WorkflowState.Failed => AgentRunSessionBundleRelease.ExceptTheLastLegOfEachBlock,
            WorkflowState.Cancelled or WorkflowState.Draft or WorkflowState.Confirmed =>
                AgentRunSessionBundleRelease.Everything,
            _ => null,
        };

        // An active state is not a terminal, and releasing on one would take the custody of an
        // execution still running. Refused loudly rather than mapped to a default.
        if (release is not { } chosen)
        {
            _logger.LogError(
                "Terminal cleanup for execution {ExecutionId} was asked to release session bundles at "
                    + "{State}, which is not a terminal state; the bundles are left alone",
                workflowExecutionId,
                terminalState);

            return;
        }

        await _bundleReaper.ReleaseExecutionAsync(workflowExecutionId, organizationId, chosen);
    }

    private async Task SettleRunAsync(
        IAgentRunExecutor executor,
        string workflowExecutionId,
        string runId,
        AgentRunTraceAnchor anchor)
    {
        bool acknowledged = await executor.SettleAbandonedQuietlyAsync(runId, anchor);

        _logger.LogInformation(
            "Terminal cleanup for execution {ExecutionId}: run '{RunId}' {Outcome}",
            workflowExecutionId,
            runId,
            acknowledged ? "settled and completed" : "settled with its acknowledgement withheld; its evidence stays for the retention reaper");
    }
}
