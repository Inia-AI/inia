namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The retention and recovery half of agent conversations. Registered on every host whether or
/// not the agent runtime is enabled, so retention keeps deleting after the runtime is turned
/// off; only the run cancels need the runtime, and they are skipped without it.
/// </summary>
public interface IAgentConversationReaper
{
    /// <summary>
    /// One retention pass. Ends every live conversation idle past its purpose's retention window
    /// (<see cref="AgentConversationPolicy.RetentionDays"/>) whose turns have all settled, then
    /// deletes every ended conversation with no turn in flight: its session bundle bytes first,
    /// then its runs' evidence rows, then the conversation, whose turns, drafts and bundle rows go
    /// by cascade. A bundle whose bytes cannot be deleted keeps its row and its conversation, so
    /// the next pass retries it. Each end and delete re-checks its condition in the statement
    /// itself, so a conversation that has just taken a message is never touched. Returns how many
    /// conversations were deleted.
    /// </summary>
    Task<int> SweepAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fails every dispatched turn still in flight longer after its dispatch than its purpose's
    /// <see cref="AgentConversationPolicy.TurnDeadline"/> plus the deployment's margin, whatever
    /// host it ran on: its run is cancelled on the runtime and its spend recorded. Returns how
    /// many turns were settled.
    /// </summary>
    Task<int> SettleOverdueTurnsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// For this host's startup: every turn dispatched to this host and still in flight belonged
    /// to the process that stopped. Each is marked Cancelled and its run cancelled on the
    /// runtime, by its run id or, when the run had not reported one yet, by the first-pass run
    /// id the turn would have started under; the spend the teardown reports is recorded. Waiting
    /// turns and other hosts' turns are left alone. Returns how many turns were settled.
    /// </summary>
    Task<int> AbandonInFlightTurnsAsync(CancellationToken cancellationToken);
}
