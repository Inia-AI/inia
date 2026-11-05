using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Documents;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.UserManagement;

/// <summary>
/// Represents an organization.
/// </summary>
public class Organization : Entity<Organization>
{
    [Required]
    [StringLength(250)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the organization is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether customers signed this organization up themselves and pay by subscription, rather than
    /// under an enterprise contract.
    /// </summary>
    public bool IsSelfServe { get; set; }

    /// <summary>
    /// The tenant API key fields that stored keys replaced. Only the startup move into stored keys
    /// reads them, and it empties them; the columns go in a later release.
    /// </summary>
    [StringLength(500)]
    public string? ParallelApiKey { get; set; }

    [StringLength(500)]
    public string? PerplexityApiKey { get; set; }

    [StringLength(500)]
    public string? FmpApiKey { get; set; }

    /// <summary>
    /// Number of approvals (or rejections) required for a pending signature override to be
    /// promoted (or cancelled). The proposer's submission counts as the first approve, so a
    /// threshold of 2 means one additional reviewer approval is needed. Minimum is 2.
    /// </summary>
    [Range(2, int.MaxValue)]
    public int SignatureReviewThreshold { get; set; } = 2;

    /// <summary>
    /// This organization's daily batch of workflow agent spend, in USD. Null uses the
    /// deployment's <c>PlatformSettings:EditorAgent:DailySpendBatchUsd</c>.
    /// </summary>
    [Range(typeof(decimal), "0.01", "100000")]
    [Precision(18, 2)]
    public decimal? EditorAgentDailySpendBatchUsd { get; set; }

    public ICollection<User> Users { get; private set; } = [];
    public ICollection<UserOrganization> UserOrganizations { get; private set; } = [];
    public ICollection<Document> Documents { get; set; } = [];

    internal Organization()
    {
    }

    public Organization(string name, string? customId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name cannot be null or empty.", nameof(name));
        }

        if (customId is not null)
        {
            Id = customId;
        }

        Name = name;
    }
}
