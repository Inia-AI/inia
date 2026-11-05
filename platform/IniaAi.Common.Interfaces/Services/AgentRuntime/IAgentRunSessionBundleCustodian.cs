using IniaAi.Domain.Entities.AgentRuntime;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Platform custody of a finished run's session bundle: fetch it from the runtime while the run
/// still holds it, check it against the digest the runtime declared, store the bytes behind the
/// file-storage abstraction, and record the pointer row that is the only index into them. The
/// shared agent-integration layer runs this between trace collection and the acknowledgement,
/// so a pointer row proves the platform held the bytes before the runtime released its copy.
/// <para>
/// Custody is best-effort by contract, which is the whole difference from trace persistence: a
/// failed fetch or a failed store logs, records no row, and never withholds the run's
/// acknowledgement. A run whose bundle was lost is a run that redoes its work from scratch,
/// which costs one leg; a withheld acknowledgement leaks runtime retention capacity on every
/// such run. A failed trace persist still withholds, unchanged — that evidence has no second
/// source.
/// </para>
/// </summary>
public interface IAgentRunSessionBundleCustodian
{
    /// <summary>
    /// Takes custody of one settled run's bundle. Returns whether a pointer row now exists:
    /// false covers a run that published no bundle at all, which is an ordinary outcome and not
    /// a fault. Never throws. <paramref name="droppedOutFileCount"/> is how many of the run's
    /// published <c>out/</c> files the trace collection could not keep (see
    /// <see cref="AgentRunTraceCollectionResult.DroppedArtifactPaths"/>) — recorded on the row
    /// because the collected rows are the platform's only copy of a member's <c>out/</c>, and a
    /// resume path must know the re-carry set would be short before it resumes a worker into a
    /// workspace missing its own files.
    /// </summary>
    Task<bool> TakeCustodyAsync(
        AgentRunSessionBundleAnchor anchor,
        string executionId,
        int attempt,
        int droppedOutFileCount = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The resolve half of a resume: the newest-round bundle held for this block below the
    /// dispatch round, or null when none is held. There is no availability flag and no
    /// marker — an absent row is the whole "start from scratch" answer. Custody owns the
    /// descending walk because custody is the only party that knows which rounds left
    /// bytes; a leg's ledger status is deliberately no part of the question.
    /// </summary>
    Task<AgentRunSessionBundle?> ResolveNewestBeforeAsync(
        string organizationId,
        string blockId,
        int dispatchRound,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest bundle held for a conversation, or null when none is held. A conversation
    /// resumes only from the turn just before the one it dispatches, so the caller compares
    /// <see cref="AgentRunSessionBundle.Round"/> against that turn.
    /// </summary>
    Task<AgentRunSessionBundle?> ResolveNewestForConversationAsync(
        string organizationId,
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The read half of a resume: the held bundle's bytes, re-verified against the digest the
    /// pointer row recorded — custody declared the digest, so custody proves it. Null when the
    /// bytes are gone or no longer match, logged; the caller then starts from scratch. Keeps
    /// the storage layout (provider, category, key) private to custody, so a resume path never
    /// names it.
    /// </summary>
    Task<byte[]?> ReadBytesAsync(
        AgentRunSessionBundle held,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Where a consumer files the bundles it takes custody of — the direct tenant anchor, the owner
/// the bundle belongs to, and the leg it was published by. The owner is exactly one of a block
/// (<see cref="ForBlock"/>, a verification leg, <see cref="Round"/> its round) or a conversation
/// (<see cref="ForConversation"/>, <see cref="Round"/> its turn number). A consumer that sets no
/// anchor buys no bundle bytes at all, which is how a plain Agent block outside any verification
/// group costs nothing.
/// </summary>
public sealed record AgentRunSessionBundleAnchor
{
    private AgentRunSessionBundleAnchor(string organizationId, string? blockId, string? conversationId, int round)
    {
        OrganizationId = organizationId;
        BlockId = blockId;
        ConversationId = conversationId;
        Round = round;
    }

    public string OrganizationId { get; }

    public string? BlockId { get; }

    public string? ConversationId { get; }

    public int Round { get; }

    public static AgentRunSessionBundleAnchor ForBlock(string organizationId, string blockId, int round)
    {
        return new AgentRunSessionBundleAnchor(organizationId, blockId, conversationId: null, round);
    }

    public static AgentRunSessionBundleAnchor ForConversation(string organizationId, string conversationId, int turn)
    {
        return new AgentRunSessionBundleAnchor(organizationId, blockId: null, conversationId, turn);
    }
}
