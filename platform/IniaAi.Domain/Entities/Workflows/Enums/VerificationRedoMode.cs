using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// How a failing member redoes its work when the verifier rejects it. Each member below states
/// the behavior it names, because the two are easy to swap and the editor's labels, this enum,
/// and the loop that will run it must agree. Closed at two values for v1 — an unrecognized enum
/// string kills the whole properties column on read, so a later third value needs an
/// environment-flag authoring gate, not just a new member here.
/// </summary>
public enum VerificationRedoMode
{
    /// <summary>
    /// Fix the flagged findings in place: the member's session resumes from its terminal
    /// checkpoint with the findings as the next turn, so the work it already did survives.
    /// </summary>
    [EnumMember(Value = nameof(Revise))]
    Revise = 0,

    /// <summary>
    /// Redo the work from a clean start: a fresh run carrying the findings as a warning
    /// preamble, keeping nothing from the rejected attempt.
    /// </summary>
    [EnumMember(Value = nameof(Scratch))]
    Scratch = 1,
}
