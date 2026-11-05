namespace IniaAi.Common.Interfaces.Services.Runs;

/// <summary>
/// Called once a run's flip to a final state has won, beside <c>IAgentRunTerminalCleanup</c>.
/// Callers invoke it from dying request scopes and teardown paths, so an implementation works in a
/// scope of its own, never throws, and is safe to call twice for the same run.
/// </summary>
public interface IRunSettlement
{
    Task SettleAsync(string workflowExecutionId, string organizationId);
}
