using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Citations.Traces;

/// <summary>
/// The citation facts one executed block carried and every figure the citation steps rejected on
/// the way, recorded during the run so a figure that reaches the report unmarked or wrongly linked
/// can be explained afterwards. <see cref="Content"/> is a <see cref="CitationTraceContent"/>. One
/// row per block, rewritten as the block's report paragraph adds its own decisions. Deliberately
/// not <c>Entity&lt;T&gt;</c>: evidence rows are never soft-deleted, they die with their block via
/// the cascade.
/// </summary>
[Index(nameof(BlockId), IsUnique = true)]
public class BlockCitationTrace
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Direct tenant anchor, stamped by the run beside <see cref="BlockId"/>.</summary>
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string BlockId { get; set; } = string.Empty;

    [ForeignKey(nameof(BlockId))]
    [InverseProperty(nameof(Workflows.Blocks.Block.CitationTrace))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    [JsonIgnore]
    [CloneIgnore]
    public Block? Block { get; set; }

    [Required]
    [JsonConverter(typeof(RawJsonStringConverter))]
    public string Content { get; set; } = string.Empty;

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
