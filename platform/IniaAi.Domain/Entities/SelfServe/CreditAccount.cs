using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// One organization's credits. Every ledger write for the organization first raises
/// <see cref="LockVersion"/>, an update that takes the row lock on every database provider, so two
/// writes cannot spend the same credits.
/// </summary>
public class CreditAccount
{
    [Key]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>Credits a new run can spend: the packs' remaining credits minus <see cref="Debt"/>.</summary>
    public int Available { get; set; }

    /// <summary>Credits held by runs and dispute freezes that are not settled yet.</summary>
    public int Pending { get; set; }

    /// <summary>Credits reversed after they were spent. The next grant pays it first.</summary>
    public int Debt { get; set; }

    public long LockVersion { get; set; }

    /// <summary>Why new runs are refused until an operator clears it; empty while runs are allowed.</summary>
    public CreditSuspension? SuspendedReason { get; set; }

    public DateTime? SuspendedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
