using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// The token usage ledger summed by organization, UTC day, usage type, workflow type and user, so a
/// usage page sums a few rows a day instead of every model call.
/// </summary>
[Index(nameof(OrganizationId), nameof(Day))]
public class TokenUsageDailyTotal : TokenUsageTotal<TokenUsageDailyTotal>, IOrganizationScoped
{
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// The UTC day the ledger rows were created.
    /// </summary>
    public DateOnly Day { get; set; }

    public UsageType UsageType { get; set; }

    [StringLength(36)]
    public string? WorkflowTypeId { get; set; }

    [StringLength(450)]
    public string? UserId { get; set; }
}
