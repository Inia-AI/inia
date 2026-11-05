using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// Records what each terminal asked cleanup to do, for the suites that assert a transition
/// reaches the seam at all. Shared because three call sites reach it and every signature change
/// otherwise costs one edit per test project.
/// </summary>
public class RecordingAgentRunTerminalCleanup : IAgentRunTerminalCleanup
{
    public List<(string ExecutionId, string OrganizationId, WorkflowState TerminalState)> CleanedUp { get; } = [];

    public Task CleanUpAsync(string workflowExecutionId, string organizationId, WorkflowState terminalState)
    {
        CleanedUp.Add((workflowExecutionId, organizationId, terminalState));

        return Task.CompletedTask;
    }
}
