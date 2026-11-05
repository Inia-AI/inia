using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// One start of an application process, with when it was last seen. The job queue lives in process
/// memory, so a run whose boot is no longer seen will never finish.
/// </summary>
[Index(nameof(LastSeenAt))]
public class HostBoot
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(100)]
    public string Application { get; set; } = string.Empty;

    [Required]
    [StringLength(250)]
    public string Machine { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
