using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Proposals;
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// The changes the editor agent proposed in one turn, as a <see cref="WorkflowProposal"/> built
/// against the draft the turn read, and what it takes to undo the changes the user accepted. Private
/// to the conversation's user and deleted with the turn.
/// </summary>
[Index(nameof(TurnId), IsUnique = true)]
[Index(nameof(ConversationId))]
public class EditorAgentProposal
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string TurnId { get; set; } = string.Empty;

    [ForeignKey(nameof(TurnId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public AgentConversationTurn? Turn { get; set; }

    /// <summary>Copied from the turn so a conversation's proposals read one table.</summary>
    [Required]
    [StringLength(450)]
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>The draft the proposal is paired against: the base of every change.</summary>
    [Required]
    [StringLength(450)]
    public string DraftId { get; set; } = string.Empty;

    /// <summary>The digest of the agent's workflow file, to tell a new proposal from one carried over.</summary>
    [Required]
    [StringLength(64)]
    public string ProposedWorkflowSha256 { get; set; } = string.Empty;

    [Required]
    public byte[] ProposalJsonCompressed { get; set; } = [];

    [NotMapped]
    public WorkflowProposal Proposal
    {
        get => WorkflowProposal.FromJson(WorkflowJsonCompression.Decompress(ProposalJsonCompressed));
        set => ProposalJsonCompressed = WorkflowJsonCompression.Compress(value.ToJson());
    }

    /// <summary>
    /// The <see cref="ProposalUndo"/> of the changes accepted and not yet undone, compressed; null
    /// when there is nothing to undo.
    /// </summary>
    public byte[]? UndoJsonCompressed { get; set; }

    [NotMapped]
    public ProposalUndo? Undo
    {
        get => UndoJsonCompressed is null ? null : ProposalUndo.FromJson(WorkflowJsonCompression.Decompress(UndoJsonCompressed));
        set => UndoJsonCompressed = value is null ? null : WorkflowJsonCompression.Compress(value.ToJson());
    }

    /// <summary>
    /// A concurrency token renewed on every write, so two editor tabs deciding about the same
    /// proposal at once cannot both record their decisions over each other.
    /// </summary>
    [ConcurrencyCheck]
    [Required]
    [StringLength(32)]
    public string Version { get; set; } = NewVersion();

    public static string NewVersion()
    {
        return Guid.NewGuid().ToString("N");
    }

    public ICollection<EditorAgentChangeDecision> Decisions { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
