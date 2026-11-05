using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.SelfServe;

[Index(nameof(HelpRequestId), nameof(CreatedAt))]
public class HelpRequestReply
{
    [Key]
    [StringLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(450)]
    public string HelpRequestId { get; set; } = string.Empty;

    [ForeignKey(nameof(HelpRequestId))]
    [DeleteBehavior(DeleteBehavior.Cascade)]
    public HelpRequest HelpRequest { get; set; } = null!;

    [Required]
    [StringLength(450)]
    public string AuthorUserId { get; set; } = string.Empty;

    [Required]
    [StringLength(HelpRequest.MESSAGE_MAX_LENGTH)]
    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the notice of the reply was emailed: to the user who filed the request for a reply from
    /// support, to the support inbox for a reply from that user.
    /// </summary>
    public DateTime? EmailedAt { get; set; }
}
