using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Serialization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// One distinct state of the workflow as the editor held it when the user sent a message to the
/// editor agent: the export JSON with test-run outputs, compressed. Private to the conversation's
/// user and deleted with the conversation. <see cref="Revision"/> counts the distinct workflow
/// states the conversation has seen; a message sent over an unchanged workflow reuses the
/// previous row, and each turn points at the row it read through
/// <see cref="AgentConversationTurn.ContextRef"/>.
/// </summary>
[Index(nameof(ConversationId), nameof(Revision), IsUnique = true)]
public class EditorAgentDraft
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string ConversationId { get; set; } = string.Empty;

    [ForeignKey(nameof(ConversationId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public AgentConversation? Conversation { get; set; }

    [Required]
    [StringLength(450)]
    public string WorkflowTypeId { get; set; } = string.Empty;

    public int Revision { get; set; }

    [Required]
    [StringLength(64)]
    public string ContentSha256 { get; set; } = string.Empty;

    [Required]
    public byte[] WorkflowJsonCompressed { get; set; } = [];

    [NotMapped]
    public string WorkflowJson
    {
        get => WorkflowJsonCompression.Decompress(WorkflowJsonCompressed);
        set => WorkflowJsonCompressed = WorkflowJsonCompression.Compress(value);
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
