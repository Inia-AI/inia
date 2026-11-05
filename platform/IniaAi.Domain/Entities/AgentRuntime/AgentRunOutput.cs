using IniaAi.Common;
using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.AgentRuntime;

/// <summary>
/// The durable output of one settled agent-family leg — the history the release path selects
/// the settled <see cref="Workflows.Responses.BlockResponse"/> from. The output is stored as
/// the leg produced it, before annotation processing strips inline markup from the response,
/// so an attempt's annotations survive with its text. Deliberately not <c>Entity&lt;T&gt;</c> —
/// attempt rows are never soft-deleted, they die with their block via the cascade.
/// </summary>
[Index(nameof(RunExecutionId))]
[Index(nameof(BlockId), nameof(Round), IsUnique = true)]
public class AgentRunOutput
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

    [Required]
    [StringLength(450)]
    public string BlockId { get; set; } = string.Empty;

    [ForeignKey(nameof(BlockId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    [JsonIgnore]
    [CloneIgnore]
    public Block? Block { get; set; }

    /// <summary>
    /// The verification leg this output settled under — the same axis the run ledger and run
    /// identity key by.
    /// </summary>
    public int Round { get; set; }

    /// <summary>
    /// The run id of the pass that produced this output, for joining the row to its ledger
    /// entry and trace files.
    /// </summary>
    [Required]
    [StringLength(128)]
    public string RunExecutionId { get; set; } = string.Empty;

    [Required]
    public string Output
    {
        get;
        set => field = value.RemoveNullCharacters();
    } = string.Empty;

    /// <summary>
    /// The agent citation facts this leg resolved, serialized, so a best-attempt
    /// restore re-applies them. Citations are resolved at run time against the staged documents
    /// and their pages, never present in <see cref="Output"/> text, so unlike the annotation
    /// markup the restore re-parses from that text they cannot be rebuilt from it. Without this
    /// column a restored attempt loses its page links and its Unverified flags. Null for a leg
    /// that resolved no citations.
    /// </summary>
    public string? CitationFactsJson { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
