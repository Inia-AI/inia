namespace IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

/// <summary>
/// The in-memory live activity feed of currently running agent runs: the executor publishes
/// mid-run digest projections into it, and the workflow-execution poll reads them out, so a
/// running block is visible before any trace row is persisted. Process-local by design — the
/// deployment runs one application instance, and the poll handler and the executor share the
/// process (jobs run in-process); on a multi-instance deployment a poll landing on another
/// instance simply sees no live entries and degrades to the persisted digest at settle.
/// Publishes for unregistered runs are dropped, and entries expire on their own so an
/// abandoned registration can never accumulate.
/// </summary>
public interface IAgentRunActivityFeed
{
    /// <summary>
    /// Announces a run before it starts. Registration carries the workflow identity the
    /// poll snapshots by; a re-registration for the same run id replaces the old entry.
    /// </summary>
    void BeginRun(AgentRunActivityRegistration registration);

    /// <summary>
    /// Publishes the latest mid-run digest projection. Dropped when the run is unknown.
    /// </summary>
    void PublishDigest(string runExecutionId, AgentActivityDigest digest);

    /// <summary>
    /// Marks the run settled. The entry stays readable for a grace window so a poll landing
    /// just after settle still delivers the final beats, then expires.
    /// </summary>
    void ConcludeRun(string runExecutionId);

    /// <summary>
    /// The live entries of one workflow execution, org-checked against the registration's
    /// own stamp so a caller can never read another tenant's feed.
    /// </summary>
    IReadOnlyList<AgentRunActivityEntry> SnapshotForExecution(string organizationId, string workflowExecutionId);

    /// <summary>
    /// The live entries of one conversation's runs, org-checked the same way.
    /// </summary>
    IReadOnlyList<AgentRunActivityEntry> SnapshotForConversation(string organizationId, string conversationId);

    /// <summary>
    /// Raised after every write to an entry, so a live surface can refresh without polling. Raised
    /// on the writer's thread; a handler must be quick and must not throw.
    /// </summary>
    event EventHandler<AgentRunActivityEntry>? EntryChanged;
}

/// <summary>
/// The workflow-side identity of a run in the live feed — stamped at registration by the
/// consumer that owns the block, the same way trace rows are stamped at attach. The round
/// completes that identity: a verification group buys several runs of the same block, and a desk
/// that cannot tell round two's leg from round one's shows one block flickering between them.
/// A consumer outside a verification loop registers round one. The role is the same letter the
/// run id is minted from, so a desk reads a verifier's leg as the verifier's rather than as one
/// more unlabelled run in a burst; a consumer that has no verification role of its own registers
/// as a worker.
/// </summary>
/// <remarks>
/// A run belongs to exactly one owner, a block (<see cref="ForBlock"/>) or a conversation turn
/// (<see cref="ForConversation"/>): the workflow fields of a conversation turn's run are null, and
/// its round is the turn number.
/// </remarks>
public sealed record AgentRunActivityRegistration
{
    private AgentRunActivityRegistration(string runExecutionId, string organizationId, int round, string role)
    {
        RunExecutionId = runExecutionId;
        OrganizationId = organizationId;
        Round = round;
        Role = role;
    }

    public string RunExecutionId { get; private init; }

    public string OrganizationId { get; }

    public string? WorkflowExecutionId { get; private init; }

    public string? BlockId { get; private init; }

    public string? BlockName { get; private init; }

    public int Round { get; }

    public string Role { get; }

    public string? ConversationId { get; private init; }

    /// <summary>The conversation's purpose, for a live surface that relays one purpose's runs.</summary>
    public string? Purpose { get; private init; }

    /// <summary>The same registration for another pass of the run, under that pass's run id.</summary>
    public AgentRunActivityRegistration ForPass(string runExecutionId)
    {
        return this with { RunExecutionId = runExecutionId };
    }

    public static AgentRunActivityRegistration ForBlock(
        string runExecutionId,
        string organizationId,
        string workflowExecutionId,
        string blockId,
        string blockName,
        int round = AgentRunIds.FIRST_ROUND,
        string role = AgentRunIds.WORKER_ROLE)
    {
        return new AgentRunActivityRegistration(runExecutionId, organizationId, round, role)
        {
            WorkflowExecutionId = workflowExecutionId,
            BlockId = blockId,
            BlockName = blockName,
        };
    }

    public static AgentRunActivityRegistration ForConversation(
        string runExecutionId,
        string organizationId,
        string purpose,
        string conversationId,
        int turn)
    {
        return new AgentRunActivityRegistration(runExecutionId, organizationId, turn, AgentRunIds.WORKER_ROLE)
        {
            Purpose = purpose,
            ConversationId = conversationId,
        };
    }
}

/// <summary>
/// One live-feed entry: the registration, the latest published digest (absent until the
/// first mid-run publish), and whether the run has settled.
/// </summary>
public sealed record AgentRunActivityEntry(
    AgentRunActivityRegistration Registration,
    AgentActivityDigest? Digest,
    bool Settled);
