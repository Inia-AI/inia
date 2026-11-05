using IniaAi.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// The token usage ledger summed per workflow execution. A row outlives its execution: purging an
/// execution clears the ledger's link to it but not the spend it ran up.
/// </summary>
[Index(nameof(WorkflowExecutionId), IsUnique = true)]
[Index(nameof(OrganizationId), nameof(WorkflowTypeId), nameof(FirstUsageAt))]
public class TokenUsageExecutionTotal : TokenUsageTotal<TokenUsageExecutionTotal>, IOrganizationScoped
{
    [Required]
    [StringLength(450)]
    public string WorkflowExecutionId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [StringLength(36)]
    public string? WorkflowTypeId { get; set; }

    public DateTime FirstUsageAt { get; set; }
}
