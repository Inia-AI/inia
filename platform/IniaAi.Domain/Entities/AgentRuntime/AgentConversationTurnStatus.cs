namespace IniaAi.Domain.Entities.AgentRuntime;

public enum AgentConversationTurnStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}

public static class AgentConversationTurnStatuses
{
    public static bool IsInFlight(this AgentConversationTurnStatus status)
    {
        return status is AgentConversationTurnStatus.Queued or AgentConversationTurnStatus.Running;
    }

    /// <summary>True while the turn waits for an earlier turn of its conversation to settle.</summary>
    public static bool IsWaiting(this AgentConversationTurn turn)
    {
        return turn.Status == AgentConversationTurnStatus.Queued && turn.DispatchedAt is null;
    }
}
