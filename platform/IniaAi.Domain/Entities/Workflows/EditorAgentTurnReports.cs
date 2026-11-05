using IniaAi.Domain.Entities.AgentRuntime;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// The past reports one editor agent turn reads as evidence: those the user picked for the
/// message, those the agent opened during the turn, and the digest of the last report request the
/// agent made in the turn, which tells a new request from one a resumed session carried over.
/// Every later turn of the conversation treats all of them as report text. Written before the turn row exists, so it
/// hangs off the conversation and is deleted with it.
/// </summary>
[Index(nameof(ConversationId))]
public class EditorAgentTurnReports
{
    public const int MAX_PICKED_LENGTH = 2000;

    [Key]
    [StringLength(450)]
    public string TurnId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string ConversationId { get; set; } = string.Empty;

    [ForeignKey(nameof(ConversationId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public AgentConversation? Conversation { get; set; }

    /// <summary>The execution ids the user picked, comma separated.</summary>
    [Required]
    [StringLength(MAX_PICKED_LENGTH)]
    public string PickedExecutionIds { get; set; } = string.Empty;

    /// <summary>The execution ids the agent opened during the turn, comma separated.</summary>
    [Required]
    [StringLength(MAX_PICKED_LENGTH)]
    public string FetchedExecutionIds { get; set; } = string.Empty;

    [StringLength(64)]
    public string? OpenRequestSha256 { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public IReadOnlyList<string> Picked => PickedExecutionIds.Split(',', StringSplitOptions.RemoveEmptyEntries);

    [NotMapped]
    public IReadOnlyList<string> Fetched => FetchedExecutionIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
}
