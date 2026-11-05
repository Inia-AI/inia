using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

[Index(nameof(OrganizationId), nameof(CreatedAt))]
[Index(nameof(Status))]
public class HelpRequest
{
    public const int MESSAGE_MAX_LENGTH = 4000;

    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>The report the request is about, when it is about one.</summary>
    [StringLength(450)]
    public string? WorkflowExecutionId { get; set; }

    [Required]
    [StringLength(MESSAGE_MAX_LENGTH)]
    public string Message { get; set; } = string.Empty;

    public HelpRequestStatus Status { get; set; }

    [StringLength(450)]
    public string? OperatorUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When support last acted on the request (took, answered or closed it) or it was closed; a customer's reply leaves it as it is.</summary>
    public DateTime? UpdatedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public ICollection<HelpRequestReply> Replies { get; set; } = [];
}
