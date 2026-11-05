using IniaAi.Domain.Entities.AgentRuntime;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// The report feedback one editor agent turn works on: the open items the user sent with the
/// "Use feedback" button, and the items the agent then suggested dismissing, with its reason.
/// Every later turn of the conversation may cite the items, and a save marks the cited ones
/// addressed. Written before the turn row exists, so it hangs off the conversation and is
/// deleted with it.
/// </summary>
[Index(nameof(ConversationId))]
public class EditorAgentTurnFeedback
{
    public const int MAX_IDS_LENGTH = 2000;

    [Key]
    [StringLength(450)]
    public string TurnId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string ConversationId { get; set; } = string.Empty;

    [ForeignKey(nameof(ConversationId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public AgentConversation? Conversation { get; set; }

    /// <summary>The feedback item ids sent with the message, comma separated, newest first.</summary>
    [Required]
    [StringLength(MAX_IDS_LENGTH)]
    public string FeedbackItemIds { get; set; } = string.Empty;

    /// <summary>How many items were open when the message was sent, the ones left out included.</summary>
    public int OpenCount { get; set; }

    /// <summary>The agent's dismiss suggestions, as JSON: <c>[{"id": ..., "reason": ...}]</c>.</summary>
    public string? DismissSuggestionsJson { get; set; }

    /// <summary>
    /// The digest of the dismiss file the suggestions came from, which tells a file the agent
    /// wrote in this turn from one a resumed session carried over from an earlier turn.
    /// </summary>
    [StringLength(64)]
    public string? DismissFileSha256 { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public IReadOnlyList<string> Sent => FeedbackItemIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
