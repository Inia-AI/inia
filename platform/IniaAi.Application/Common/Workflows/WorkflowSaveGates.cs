using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The gates a template save refuses on before it writes: the agent-block guard and the screening
/// guard. Reports every failure; the save pipeline decides which of them stop it.
/// </summary>
public sealed class WorkflowSaveGates
{
    private readonly AgentRuntimeSettings _agentRuntime;
    private readonly ModelRoutingSettings? _modelRouting;
    private readonly IAgentRunLimitsProvider? _runLimitsProvider;
    private readonly WorkflowExecutionJanitorSettings? _janitor;
    private readonly int? _maxBlockParallelism;

    public WorkflowSaveGates(
        AgentRuntimeSettings agentRuntime,
        IAgentRunLimitsProvider? runLimitsProvider,
        WorkflowExecutionJanitorSettings? janitor,
        int? maxBlockParallelism,
        ModelRoutingSettings? modelRouting = null)
    {
        _agentRuntime = agentRuntime;
        _modelRouting = modelRouting;
        _runLimitsProvider = runLimitsProvider;
        _janitor = janitor;
        _maxBlockParallelism = maxBlockParallelism;
    }

    /// <summary>
    /// The runtime's enforceable limits, null when the runtime is unreachable or the limits cache
    /// stale; the agent guard then skips the ceiling check, because a runtime outage must never
    /// stop an author saving.
    /// </summary>
    public AgentRunLimits? RunLimits => _runLimitsProvider?.Enforceable;

    /// <param name="organizationId">The saving organization, whose agent model tiers apply; null uses the deployment's.</param>
    public List<WorkflowCheckFailure> Check(Workflow workflow, AgentRunLimits? runLimits, string? organizationId = null)
    {
        List<WorkflowCheckFailure> failures = AgentBlockGuard
            .ValidateGraph(
                workflow,
                _agentRuntime,
                runLimits,
                _janitor,
                _maxBlockParallelism,
                AgentModelTiers.For(_agentRuntime, _modelRouting, organizationId))
            .ConvertAll(static violation => new WorkflowCheckFailure(WorkflowCheck.AgentGates, violation.Message));

        return failures;
    }
}
