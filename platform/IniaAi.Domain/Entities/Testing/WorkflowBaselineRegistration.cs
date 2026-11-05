using System.ComponentModel.DataAnnotations;
using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.Entities.Testing;

/// <summary>
/// Immutable, supersession-chained registry of blessed baselines, for the bank audit trail.
/// One row per blessing of a baseline execution. Staleness is computed at read (blessed content
/// hash vs the live template head hash), never stored. Supersession key is
/// (OrganizationId, WorkflowTypeId, CounterpartyId).
/// </summary>
[Index(nameof(OrganizationId), nameof(WorkflowTypeId))]
public class WorkflowBaselineRegistration : Entity<WorkflowBaselineRegistration>, IOrganizationScoped, IIdentifiable
{
    [Required]
    public string WorkflowTypeId { get; set; } = string.Empty;

    [Required]
    public string OrganizationId { get; set; } = string.Empty;

    // Non-nullable with an empty-string sentinel for "no counterparty": PostgreSQL treats NULLs
    // as distinct in unique indexes, which would let multiple Active rows share a key.
    [Required(AllowEmptyStrings = true)]
    public string CounterpartyId { get; set; } = string.Empty;

    public string? CounterpartyName { get; set; }

    [Required]
    public string ExecutionId { get; set; } = string.Empty;

    [Required]
    public string MarkedByUserId { get; set; } = string.Empty;

    [Required]
    public DateTime MarkedAt { get; set; }

    [StringLength(64)]
    public string? BlessedContentHash { get; set; }

    [StringLength(450)]
    public string? BlessedVersionId { get; set; }

    public BaselineRegistrationState State { get; set; } = BaselineRegistrationState.Active;

    [StringLength(450)]
    public string? SupersededByRegistrationId { get; set; }

    public string? SourceInstance { get; set; }

    public string? SourceExecutionId { get; set; }

    [StringLength(64)]
    public string? SourceBlessedContentHash { get; set; }
}

/// <summary>
/// Lifecycle state of a baseline registration. Stale is derived at read, not stored here.
/// </summary>
public enum BaselineRegistrationState
{
    Active,
    Superseded,
}
