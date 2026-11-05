using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.AgentRuntime;

/// <summary>
/// One collected file of an agent run — its trace pack (<c>trace/</c> paths) or its published
/// artifacts (<c>out/</c> paths) — persisted verbatim by the platform after the run settles.
/// The evidence record of an agent run, not of a workflow block: <see cref="BlockId"/> is
/// optional so non-workflow consumers of the runtime can persist their runs' traces too.
/// Deliberately not <c>Entity&lt;T&gt;</c> — evidence rows are never soft-deleted, they die with
/// their block via the cascade.
/// </summary>
[Index(nameof(BlockId))]
[Index(nameof(RunExecutionId))]
[Index(nameof(RunExecutionId), nameof(Attempt), nameof(Path), IsUnique = true)]
public class AgentRunTraceFile
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Direct tenant anchor, stamped by the consumer beside <see cref="BlockId"/> — never derived
    /// through the optional <see cref="Block"/> chain, so block-less rows stay org-queryable.
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

    [Required]
    [StringLength(128)]
    public string RunExecutionId { get; set; } = string.Empty;

    public int Attempt { get; set; }

    [Required]
    [StringLength(450)]
    public string Path { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    [Required]
    public byte[] Content { get; set; } = [];

    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}
