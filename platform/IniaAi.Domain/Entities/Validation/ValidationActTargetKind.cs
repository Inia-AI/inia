namespace IniaAi.Domain.Entities.Validation;

/// <summary>
/// What the act a run belongs to was taken over. Stamped on every run the act creates, so an
/// act can be listed under the thing it ran rather than inferred from the one target that
/// happened to have a column of its own.
/// </summary>
public enum ValidationActTargetKind
{
    /// <summary>
    /// The act predates the stamping, so what it ran was never recorded. A run carrying a suite
    /// id is still read as that suite's act; the rest stay unknown rather than guessed.
    /// </summary>
    Unknown = 0,

    /// <summary>A curated suite; the target id is its suite id.</summary>
    Suite = 1,

    /// <summary>A workflow type's automatic suite; the target id is the workflow type id.</summary>
    Workflow = 2,

    /// <summary>The active cases bound to no workflow type; the target has no id.</summary>
    UnassignedWorkflow = 3,

    /// <summary>The cases the caller named; the target has no id.</summary>
    Cases = 4,

    /// <summary>Every active case of the organization; the target has no id.</summary>
    EveryActiveCase = 5,
}
