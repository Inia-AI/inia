using ErrorOr;
using IniaAi.Domain.Entities.AgentRuntime;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// The shared layer's conversation turns: numbering, the per-user running cap, the order within
/// a conversation, starting waiting turns, cancel, and ending a conversation. A consumer
/// registers an <see cref="IAgentConversationConsumer"/> for its purpose and supplies what a turn
/// reads; everything a turn needs to start, run and settle lives here.
/// <para>
/// Single process by design: turns run as jobs in the host's memory, and the cap and the
/// numbering are checked under a process-wide lock against the database. A second host racing
/// the same conversation is caught by the unique turn number and answered with
/// <see cref="AgentConversationErrors.Busy"/>; a cap can be exceeded by one turn per extra host.
/// </para>
/// </summary>
public interface IAgentConversationTurns
{
    /// <summary>
    /// Records a message as the conversation's next turn and returns it. When the conversation is
    /// idle the turn starts at once; when an earlier turn is still in flight it waits, and starts
    /// on its own when the turns before it have settled, in order. A waiting turn survives a
    /// restart. Refused with <see cref="AgentConversationErrors.TooManyRunning"/> only when the
    /// turn would start now and the user already has the purpose's cap of turns running.
    /// </summary>
    Task<ErrorOr<AgentConversationTurn>> SubmitAsync(
        AgentConversationSubmission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a turn. A waiting turn is marked Cancelled. A turn this host runs is cancelled
    /// through its job, which bills the spend and settles the row. A turn another host runs is
    /// cancelled on the runtime by its run id, and that host settles it; one that has no run id
    /// yet is refused with <see cref="AgentConversationErrors.StartingElsewhere"/>. A turn whose
    /// host is this one but whose job is gone is settled here, its run cancelled and billed.
    /// </summary>
    Task<ErrorOr<Success>> CancelAsync(AgentConversationTurnKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends a conversation: it takes no more messages, its waiting turns are cancelled, a turn
    /// already running finishes, and the next retention sweep deletes it.
    /// </summary>
    Task EndAsync(
        string organizationId,
        string userId,
        string purpose,
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts every waiting turn whose predecessors have settled, within each user's cap. For a
    /// host's startup and its periodic sweep. Returns how many turns started.
    /// </summary>
    Task<int> DispatchReadyAsync(CancellationToken cancellationToken = default);
}

/// <param name="ContextRef">The consumer's reference for what the turn reads, stored on the turn.</param>
public sealed record AgentConversationSubmission(
    string OrganizationId,
    string UserId,
    string Purpose,
    string ConversationId,
    string Message,
    string ModelId,
    string? ContextRef)
{
    /// <summary>
    /// The new turn's id, for a consumer that records its own rows about the turn before
    /// submitting it, so a turn that starts at once finds them. Null takes a fresh id.
    /// </summary>
    public string? TurnId { get; init; }
}

public sealed record AgentConversationTurnKey(string OrganizationId, string UserId, string Purpose, string TurnId);

/// <summary>
/// What a consumer of agent conversations supplies to the shared layer, registered once per
/// purpose. Resolved inside a scope, so it may depend on scoped services.
/// </summary>
public interface IAgentConversationConsumer
{
    /// <summary>The <see cref="AgentConversation.Purpose"/> this consumer owns.</summary>
    string Purpose { get; }

    AgentConversationPolicy Policy { get; }

    /// <summary>How the purpose's spend is recorded, also for a turn settled by recovery.</summary>
    AgentConversationBilling Billing(AgentConversation conversation);

    /// <summary>Builds what the turn's run reads. A fault here settles the turn Failed.</summary>
    Task<AgentConversationTurnRequest> StageTurnAsync(
        AgentConversationTurn turn,
        AgentConversation conversation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Drops what the consumer keeps of turns settled before the retention window that may hold
    /// what the agent read. Called on every sweep for every such turn of a conversation still in
    /// use, so it must be cheap to repeat.
    /// </summary>
    Task PruneTurnsAsync(IReadOnlyList<string> turnIds, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <param name="RetentionDays">How long a conversation may sit idle before it ends.</param>
/// <param name="MaxRunningTurnsPerUser">How many dispatched turns one user may have in flight
/// across the purpose's conversations. Waiting turns do not count.</param>
/// <param name="TurnDeadline">How long one turn may take, admission included. The overdue sweep
/// fails a turn still in flight this long after dispatch, plus the deployment's margin.</param>
public sealed record AgentConversationPolicy(int RetentionDays, int MaxRunningTurnsPerUser, TimeSpan TurnDeadline);

/// <summary>
/// The in-process feed of turn status changes, for a consumer's live surface. Raised on a
/// background thread; a handler must be quick and must not throw.
/// </summary>
public interface IAgentConversationEvents
{
    event EventHandler<AgentConversationTurnChanged>? TurnChanged;

    void Publish(AgentConversationTurnChanged change);
}

public sealed record AgentConversationTurnChanged(
    string OrganizationId,
    string Purpose,
    string ConversationId,
    string TurnId,
    int TurnNumber,
    AgentConversationTurnStatus Status);

public static class AgentConversationErrors
{
    public const string NOT_FOUND_CODE = "AgentConversation.NotFound";
    public const string TOO_MANY_RUNNING_CODE = "AgentConversation.TooManyRunning";
    public const string BUSY_CODE = "AgentConversation.Busy";
    public const string STARTING_ELSEWHERE_CODE = "AgentConversation.StartingElsewhere";

    public static Error NotFound => Error.NotFound(
        NOT_FOUND_CODE, "That conversation does not exist, has ended, or is not yours.");

    public static Error TooManyRunning(int cap) => Error.Conflict(
        TOO_MANY_RUNNING_CODE, $"You already have {cap} messages running. Wait for one to finish.");

    public static Error Busy => Error.Conflict(
        BUSY_CODE, "Another message was being recorded at the same moment. Send it again.");

    public static Error StartingElsewhere => Error.Conflict(
        STARTING_ELSEWHERE_CODE, "The message is still starting on another server. Try again in a moment.");
}

/// <summary>The plain-language reasons a turn shows when it did not complete.</summary>
public static class AgentConversationTurnErrors
{
    public const string CANCELLED = "The message was stopped.";
    public const string ABANDONED = "The application restarted while this message was running.";
    public const string OVERDUE = "The message took too long and was stopped.";
    public const string RUNTIME_OFF = "The agent runtime is not available on this deployment.";
    public const string STAGING_FAILED = "The message could not be prepared for the agent.";
    public const string START_FAILED = "The message could not be started.";
    public const string INTERNAL_FAILURE = "Something went wrong while the agent answered this message.";
    public const string CONVERSATION_ENDED = "The conversation ended before this message started.";
}
