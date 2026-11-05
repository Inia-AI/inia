namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Whether a document class must be supplied at execution time.
/// </summary>
/// <remarks>
/// Mirror of <c>IniaAi.Domain.Entities.Workflows.Enums.DocumentClassRequirement</c> — the two are
/// cast between each other by integer value at the application boundary. Drift is guarded by
/// <c>DocumentClassRequirementEnumAlignmentTests</c>; keep both enums in lockstep.
/// </remarks>
public enum DocumentClassRequirement
{
    /// <summary>At least one document of this class must be provided.</summary>
    Required,

    /// <summary>Documents of this class are suggested but not enforced.</summary>
    Suggested,

    /// <summary>Documents of this class are optional.</summary>
    Optional,
}
