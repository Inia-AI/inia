namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The retention half of bundle custody: bundles exist to let a member redo its work by
/// continuing its own session, so they stop being worth their bytes the moment the execution
/// they belong to can no longer redo anything. Two sweeps close that window — the execution's
/// own terminal, and a time bound for everything the terminal never reached. Best-effort
/// throughout: a storage delete that cannot land is logged and the sweep continues, because a
/// stranded blob is cheaper than a sweep that stops at the first fault.
/// </summary>
public interface IAgentRunSessionBundleReaper
{
    /// <summary>
    /// Releases the bundles of one workflow execution at its terminal. Never throws.
    /// </summary>
    Task ReleaseExecutionAsync(
        string workflowExecutionId,
        string organizationId,
        AgentRunSessionBundleRelease release);

    /// <summary>
    /// Releases a conversation's bundles of turns before <paramref name="keepFromTurn"/>, so a
    /// conversation holds only the bundle its next turn resumes from. Never throws.
    /// </summary>
    Task ReleaseConversationBeforeAsync(string conversationId, int keepFromTurn);

    /// <summary>
    /// Deletes every pointer row taken into custody before <paramref name="createdBefore"/>,
    /// with its bytes, and returns how many rows this sweep removed.
    /// </summary>
    Task<int> SweepExpiredAsync(DateTime createdBefore, CancellationToken cancellationToken);
}

/// <summary>
/// How much of an execution's custody survives its terminal.
/// </summary>
public enum AgentRunSessionBundleRelease
{
    /// <summary>
    /// The execution finished or was cancelled: nothing will ever redo a leg of it, so every
    /// bundle it holds goes, the successful terminal included.
    /// </summary>
    Everything,

    /// <summary>
    /// The execution failed: a person may still want to see what each member had reached, so
    /// each block keeps the bundle of the last round it ran and every earlier round of that
    /// block goes. This caps a failed six-member group at seven bundles — one per member, plus
    /// the verifier's last leg.
    /// <para>
    /// The last round it ran, deliberately, and not the round whose work stands: best-attempt
    /// retention can restore a member to an earlier round, so the two differ. Nothing resumes a
    /// failed execution — the bundles are kept to be read, and the last leg is the one a reader
    /// is asking about.
    /// </para>
    /// </summary>
    ExceptTheLastLegOfEachBlock,
}
