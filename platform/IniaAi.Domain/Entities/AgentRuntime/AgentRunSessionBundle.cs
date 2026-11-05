using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.AgentRuntime;

/// <summary>
/// The pointer to one agent run's session bundle, held in platform custody so a later leg can
/// continue that session instead of starting over. The bytes live behind the file-storage
/// abstraction under <see cref="StorageProvider"/> and <see cref="StorageKey"/>; this row is
/// the only index into them. A row exists exactly when the platform durably holds the bundle —
/// there is no availability flag, so an absent row is the whole "nothing to resume from"
/// answer. Deliberately not <c>Entity&lt;T&gt;</c> — custody rows are never soft-deleted, they
/// die with their block or their conversation via the cascade, and a soft-deleted row would keep
/// pointing at bytes nobody would ever reap.
/// <para>
/// Every row belongs to exactly one owner: a workflow <see cref="Block"/> (a verification leg) or
/// an <see cref="AgentConversation"/> (a conversation turn). The custodian refuses an anchor that
/// names both or neither.
/// </para>
/// </summary>
/// <remarks>
/// Keyed on (block, round) like <see cref="AgentRunOutput"/>, and for the same reason: a
/// verification loop can restore a member to an earlier round's attempt, so the leg whose work
/// currently stands is not always the latest one. Retention decides which attempt RELEASES;
/// the continuation planner resumes the newest round that left a bundle, whatever any leg's
/// status, because that transcript carries the most sunk work. Re-running a block
/// at the same round supersedes that round's row and deletes the superseded bytes.
/// </remarks>
[Index(nameof(RunExecutionId))]
[Index(nameof(BlockId), nameof(Round), IsUnique = true)]
[Index(nameof(ConversationId), nameof(Round), IsUnique = true)]
[Index(nameof(CreatedAt))]
public class AgentRunSessionBundle
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Direct tenant anchor, stamped by the writer — never derived through the block chain.
    /// </summary>
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [StringLength(450)]
    public string? BlockId { get; set; }

    [ForeignKey(nameof(BlockId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    [JsonIgnore]
    [CloneIgnore]
    public Block? Block { get; set; }

    [StringLength(450)]
    public string? ConversationId { get; set; }

    [ForeignKey(nameof(ConversationId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    [JsonIgnore]
    [CloneIgnore]
    public AgentConversation? Conversation { get; set; }

    /// <summary>
    /// The verification leg this bundle was published by — the same axis the run ledger, the
    /// run identity and <see cref="AgentRunOutput"/> key by. For a conversation row, the turn
    /// number.
    /// </summary>
    public int Round { get; set; }

    /// <summary>
    /// The run whose session this bundle holds, for joining the row to its ledger entry, its
    /// output row and its trace files.
    /// </summary>
    [Required]
    [StringLength(128)]
    public string RunExecutionId { get; set; } = string.Empty;

    public int Attempt { get; set; }

    /// <summary>
    /// Which checkpoint of the run this is, in the runtime's own numbering: the terminal bundle
    /// is <c>-1</c> and pause checkpoints count from zero. Only terminal bundles are taken into
    /// custody today; the column keeps the row honest about which one it holds.
    /// </summary>
    public int Segment { get; set; }

    /// <summary>
    /// The digest the runtime declared, re-computed over the fetched bytes before this row was
    /// written. A row therefore proves the platform holds bytes that hashed correctly.
    /// </summary>
    [Required]
    [StringLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    [Required]
    [StringLength(50)]
    public string StorageProvider { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>
    /// The bundle's own four-part version stamp as the manifest carried it — bundle format,
    /// sidecar build, SDK pin, and the model that governed the run. Null for a bundle written
    /// by a sidecar older than the stamp, which still reads: it reads as unstamped, and what a
    /// reader does about that belongs to the resume path, not to custody.
    /// </summary>
    [StringLength(1024)]
    public string? VersionStamp { get; set; }

    /// <summary>
    /// How many of the leg's published <c>out/</c> files the trace collection could not keep —
    /// the collected rows are the platform's only copy of a member's <c>out/</c>, so a non-zero
    /// count means a continuation re-carrying this leg would resume a worker into a workspace
    /// missing its own files; the resume path degrades to scratch and says why. Recorded here
    /// beside the digest and the stamp because it is the same question — is this row usable to
    /// resume from — answered at the same custody moment.
    /// </summary>
    public int DroppedOutFileCount { get; set; }

    /// <summary>
    /// When the platform took custody. The anchor the retention sweep measures its window from.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
