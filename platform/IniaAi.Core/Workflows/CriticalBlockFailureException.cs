using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Core.Workflows;

/// <summary>
/// The block-level failure that tripped the run's abort: what failed and how to explain it. Set
/// before the abort token is cancelled so the unwind can tell an internal abort apart from a
/// caller's cancellation.
/// </summary>
/// <param name="BlockName">Display name of the block whose failure aborted the run.</param>
/// <param name="Explanation">Human-readable reason, already naming the block.</param>
/// <param name="ClassifiedFailureKind">
/// Set when the block's exception itself classified the run's failure — a verdict-driven abort
/// like verification's unsatisfiable. The kind survives the abort instead of being relabeled
/// <see cref="WorkflowExecutionFailureKinds.CRITICAL_BLOCK_FAILURE"/>.
/// </param>
/// <param name="LoggedExplanation">
/// The explanation without the run's content, for a log line; null when the explanation holds none.
/// </param>
public sealed record CriticalBlockFailure(
    string BlockName,
    string Explanation,
    string? ClassifiedFailureKind = null,
    string? LoggedExplanation = null)
{
    public string ExplanationForLogs => LoggedExplanation ?? Explanation;
}

/// <summary>
/// Thrown when a block whose failure must not be absorbed failed and aborted the run. Replaces the
/// bare cancellation the abort token produces, which the job layer would otherwise mislabel
/// Cancelled — the run ends Failed, carrying the kind and detail onto the execution row.
/// </summary>
public sealed class CriticalBlockFailureException : Exception, IWorkflowExecutionFailure
{
    public CriticalBlockFailureException(
        CriticalBlockFailure failure,
        IReadOnlyList<string> recoveredSiblingResults,
        IReadOnlyList<string> lostSiblingResults,
        Exception? innerException = null)
        : base(BuildMessage(failure.ExplanationForLogs, recoveredSiblingResults, lostSiblingResults), innerException)
    {
        FailureDetail = BuildMessage(failure.Explanation, recoveredSiblingResults, lostSiblingResults);
        FailureKind = failure.ClassifiedFailureKind ?? WorkflowExecutionFailureKinds.CRITICAL_BLOCK_FAILURE;
        RecoveredSiblingResults = recoveredSiblingResults;
        LostSiblingResults = lostSiblingResults;
    }

    public string FailureKind { get; }

    public string FailureDetail { get; }

    /// <summary>Sibling blocks whose in-flight persist the abort cancelled and the replay recovered.</summary>
    public IReadOnlyList<string> RecoveredSiblingResults { get; }

    /// <summary>Sibling blocks whose result the replay could not land; those results are gone.</summary>
    public IReadOnlyList<string> LostSiblingResults { get; }

    private static string BuildMessage(
        string explanation,
        IReadOnlyList<string> recoveredSiblingResults,
        IReadOnlyList<string> lostSiblingResults)
    {
        List<string> parts = [explanation];

        BlockNameCategory.AppendTo(parts, "lost to the abort", lostSiblingResults);
        BlockNameCategory.AppendTo(parts, "replayed after the abort", recoveredSiblingResults);

        return string.Join(" ", parts);
    }
}
