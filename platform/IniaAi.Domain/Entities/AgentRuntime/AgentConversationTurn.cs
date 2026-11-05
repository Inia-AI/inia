using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.AgentRuntime;

/// <summary>
/// One message of an <see cref="AgentConversation"/> and the run that answered it. The row is
/// the turn's whole state, so a page that reloads mid-turn reattaches by reading it.
/// <para>
/// A Queued turn is either waiting (<see cref="DispatchedAt"/> null: an earlier turn of the
/// conversation is still in flight, and this one starts when it settles) or dispatched to a
/// host's job queue (<see cref="DispatchedAt"/>, <see cref="JobId"/> and
/// <see cref="HostInstanceId"/> set). Only dispatched turns count as running.
/// </para>
/// </summary>
[Index(nameof(ConversationId), nameof(Number), IsUnique = true)]
[Index(nameof(UserId), nameof(Status))]
[Index(nameof(Status), nameof(HostInstanceId))]
public class AgentConversationTurn
{
    public const int ERROR_MAX_LENGTH = 2000;
    public const int HOST_INSTANCE_ID_MAX_LENGTH = 128;

    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string ConversationId { get; set; } = string.Empty;

    [ForeignKey(nameof(ConversationId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public AgentConversation? Conversation { get; set; }

    /// <summary>
    /// Copied from the conversation so the per-user running-turn cap reads one table.
    /// </summary>
    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>The turn's position in its conversation, counting from one.</summary>
    public int Number { get; set; }

    [Required]
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>The agent's answer as markdown, set when the turn completes.</summary>
    public string? AnswerMarkdown { get; set; }

    /// <summary>
    /// A concurrency token: every tracked write checks the status it read, so two parties that
    /// settle the same turn at once cannot both win, and a settled turn is never moved back.
    /// </summary>
    [ConcurrencyCheck]
    public AgentConversationTurnStatus Status { get; set; } = AgentConversationTurnStatus.Queued;

    [Required]
    [StringLength(256)]
    public string ModelId { get; set; } = string.Empty;

    /// <summary>The run id of the turn's latest pass; null until a run starts.</summary>
    [StringLength(128)]
    public string? RunExecutionId { get; set; }

    /// <summary>How many runs the turn bought, retries and continuation fallbacks included.</summary>
    public int RunCount { get; set; }

    /// <summary>Whether the turn resumed the previous turn's session rather than starting fresh.</summary>
    public bool ResumedSession { get; set; }

    [Precision(18, 6)]
    public decimal CostUsd { get; set; }

    [StringLength(ERROR_MAX_LENGTH)]
    public string? Error { get; set; }

    /// <summary>
    /// The settled activity digest as JSON: the steps the agent took, for the panel to replay.
    /// </summary>
    public string? ActivityJson { get; set; }

    /// <summary>The in-memory job running the turn, for cancelling it on the host that runs it.</summary>
    [StringLength(64)]
    public string? JobId { get; set; }

    /// <summary>
    /// The host process the turn was dispatched to. Startup recovery abandons only its own
    /// host's turns, and a cancel on another host goes through the runtime instead.
    /// </summary>
    [StringLength(HOST_INSTANCE_ID_MAX_LENGTH)]
    public string? HostInstanceId { get; set; }

    /// <summary>
    /// The consumer's reference for what the turn read, for the workflow editor the draft row it
    /// staged. No foreign key: the consumer's rows hang off the conversation.
    /// </summary>
    [StringLength(450)]
    public string? ContextRef { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the turn was handed to a host's job queue; null while it waits.</summary>
    public DateTime? DispatchedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
