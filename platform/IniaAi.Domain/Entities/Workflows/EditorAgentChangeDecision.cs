using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>What the user decided about one change of an <see cref="EditorAgentProposal"/>.</summary>
[Index(nameof(ProposalId), nameof(ChangeId), IsUnique = true)]
public class EditorAgentChangeDecision
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string ProposalId { get; set; } = string.Empty;

    [ForeignKey(nameof(ProposalId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public EditorAgentProposal? Proposal { get; set; }

    [Required]
    [StringLength(450)]
    public string ChangeId { get; set; } = string.Empty;

    public EditorAgentChangeDecisionStatus Status { get; set; }

    /// <summary>The stable id an accepted added block or section received.</summary>
    [StringLength(450)]
    public string? MintedStableId { get; set; }

    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When a save first carried this accepted change; null until then.</summary>
    public DateTime? SavedAt { get; set; }
}

public enum EditorAgentChangeDecisionStatus
{
    Accepted = 0,
    Rejected = 1,
    Undone = 2,
}
