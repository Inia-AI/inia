using IniaAi.Domain.Abstractions;
using IniaAi.Domain.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// A named, ordered list of validation case ids. The ids are loose on purpose: reverse
/// lookup ("which suites hold this case") is a declared non-requirement, and a dangling
/// id renders as "case deleted" instead of constraining case lifecycle.
/// </summary>
[Index(nameof(OrganizationId), nameof(SourceInstance), nameof(SourceSuiteId))]
public class ValidationSuite : Entity<ValidationSuite>, IOrganizationScoped, IIdentifiable
{
    [Required]
    [StringLength(450)]
    public string OrganizationId { get; set; } = string.Empty;

    [Required]
    [StringLength(250)]
    public string Name { get; set; } = string.Empty;

    public string CaseIdsJson { get; set; } = "[]";

    /// <summary>
    /// Bumped by every write of <see cref="CaseIdsJson"/>, so two writers that read the same
    /// membership cannot both save: the second is refused rather than silently erasing the
    /// first's change.
    /// </summary>
    [ConcurrencyCheck]
    public int MembershipRevision { get; set; }

    /// <summary>
    /// The deployment a suite bundle was exported from, for a suite that arrived by import;
    /// null for a native suite. With <see cref="SourceSuiteId"/> this is the cross-instance
    /// identity a re-import supersedes on — the same pair, and the same rule, a case carries.
    /// </summary>
    [StringLength(ValidationCase.MAX_SOURCE_INSTANCE_LENGTH)]
    public string? SourceInstance { get; set; }

    /// <summary>
    /// The suite's id on the instance that exported it; null for a native suite.
    /// </summary>
    [StringLength(450)]
    public string? SourceSuiteId { get; set; }
}
