using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.DailyAllowances;

/// <summary>
/// How many requests of one kind a user made on one UTC day. The key is the unique index that the
/// allowance's one-statement take relies on.
/// </summary>
[PrimaryKey(nameof(UserId), nameof(Kind), nameof(Day))]
public class DailyAllowanceUse
{
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    public DailyAllowanceKind Kind { get; set; }

    /// <summary>The UTC day the requests were made.</summary>
    public DateOnly Day { get; set; }

    public int Count { get; set; }
}
