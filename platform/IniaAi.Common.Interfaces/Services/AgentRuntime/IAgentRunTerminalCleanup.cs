using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The terminal-transition half of run hygiene: when a workflow execution reaches a terminal
/// state — Failed, Cancelled, a critical abort, or the janitor's own mark — this enumerates the
/// agent runs recorded on the execution's persisted blocks that never had a complete accepted
/// (<c>AgentRunPass.Unacknowledged</c>) and settles each with the quiet verbs: acknowledge
/// first, cancel when the runtime did not accept the complete (a still-live run). Only rows
/// that may still be spending are settled — a settled run whose complete was withheld keeps
/// its evidence pack for the runtime's retention reaper. Best-effort
/// by contract: reads its own fresh scope, never throws, and logs what it settled — safe to
/// call from a dying request scope or teardown. Registered beside
/// <see cref="IAgentRunExecutor"/> exactly when the runtime is enabled; callers hold it as
/// optional and skip when absent.
/// <para>
/// The same transition also closes the execution's session-bundle custody, which is why the
/// terminal state is a parameter: a Failed execution keeps the last leg of each block, because
/// a person may still want to read what each member had reached, while every other terminal —
/// the successful one included — releases the lot. That second half covers every run of the
/// execution, acknowledged ones too, so it is a wider enumeration than the settling half.
/// </para>
/// </summary>
public interface IAgentRunTerminalCleanup
{
    Task CleanUpAsync(string workflowExecutionId, string organizationId, WorkflowState terminalState);
}
