using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.Entities.DataManagement;

/// <summary>
/// Per-organization soft-delete cleanup configuration.
/// When OrganizationId is null, this represents a global default.
/// </summary>
public class OrganizationCleanupSettings : Entity<OrganizationCleanupSettings>
{
    /// <summary>
    /// The organization this setting applies to.
    /// NULL indicates a global default that applies to all organizations.
    /// </summary>
    [StringLength(450)]
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Number of days to retain soft-deleted records before hard-deleting them.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Day of the week to run cleanup (0 = Sunday, 6 = Saturday).
    /// </summary>
    public DayOfWeek CleanupDayOfWeek { get; set; } = DayOfWeek.Sunday;

    /// <summary>
    /// Hour of the day (UTC, 0-23) to run cleanup.
    /// </summary>
    [Range(0, 23)]
    public int CleanupHourUtc { get; set; } = 2;

    /// <summary>
    /// Whether cleanup is enabled for this organization.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether this record has explicitly configured settings.
    /// False means it was auto-created just to track LastCleanupAt,
    /// and the background service should fall through to global/hardcoded defaults.
    /// </summary>
    public bool HasExplicitSettings { get; set; }

    /// <summary>
    /// Timestamp of the last successful cleanup run.
    /// </summary>
    public DateTime? LastCleanupAt { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public Organization? Organization { get; set; }
}
