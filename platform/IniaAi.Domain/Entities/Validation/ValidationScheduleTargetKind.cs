namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// Which of the two kinds of suite a schedule names. One kind and one id rather than a nullable
/// column per kind, so a schedule that names both, or neither, cannot be written.
/// </summary>
public enum ValidationScheduleTargetKind
{
    /// <summary>A curated suite: <c>TargetId</c> is a <see cref="ValidationSuite"/> id.</summary>
    Suite = 0,

    /// <summary>A workflow's automatic suite: <c>TargetId</c> is a workflow type id.</summary>
    Workflow = 1,
}
