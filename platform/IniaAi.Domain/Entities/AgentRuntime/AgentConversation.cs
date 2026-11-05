using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.AgentRuntime;

/// <summary>
/// A conversation one user holds with an agent on the agent runtime: each message is a turn, and
/// each turn is a run that continues the previous turn's session. The shared agent-integration
/// layer owns it, so any consumer can hold one; <see cref="Purpose"/> names the consumer and
/// <see cref="SubjectId"/> the thing the conversation is about (for the workflow editor, the
/// workflow type). It ends when the user starts a new one or when it has been idle past its
/// purpose's retention window; an ended conversation takes no more messages and is hard-deleted
/// by the next retention sweep, its turns, drafts and session bundle rows with it by cascade.
/// </summary>
[Index(nameof(UserId), nameof(Purpose), nameof(SubjectId))]
[Index(nameof(LastActivityAt))]
[Index(nameof(EndedAt))]
public class AgentConversation
{
    public const int PURPOSE_MAX_LENGTH = 16;

    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The consumer's short slug. It also prefixes every run id the conversation buys, so it uses
    /// the run id alphabet.
    /// </summary>
    [Required]
    [StringLength(PURPOSE_MAX_LENGTH)]
    public string Purpose { get; set; } = string.Empty;

    [StringLength(450)]
    public string? SubjectId { get; set; }

    [Required]
    [StringLength(256)]
    public string ModelId { get; set; } = string.Empty;

    [StringLength(32)]
    public string? Effort { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the user last sent a message or a turn last settled. Retention measures from here.
    /// </summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the conversation stopped taking messages: the user started a new one, or retention
    /// found it idle. Null while it is live. A concurrency token, so a message recorded against a
    /// conversation that ended meanwhile fails instead of landing in one about to be deleted.
    /// </summary>
    [ConcurrencyCheck]
    public DateTime? EndedAt { get; set; }

    public ICollection<AgentConversationTurn> Turns { get; set; } = [];
}
