using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Core.Workflows;

/// <summary>
/// Thrown at the end of a persisted workflow run when the execution's stored results cannot be
/// trusted: block results failed to persist (even if a retry later recovered them), blocks never
/// reached a terminal state, or failures escaped the block runner. Callers treat it like any other
/// execution error and mark the run Failed — the one thing this exception exists to prevent is a
/// run with lost results concluding as finished.
/// </summary>
public sealed class WorkflowResultsLostException : Exception, IWorkflowExecutionFailure
{
    public WorkflowResultsLostException(
        IReadOnlyList<string> permanentlyLostBlocks,
        IReadOnlyList<string> recoveredBlocks,
        IReadOnlyList<string> unconcludedBlocks,
        int schedulerEscapedCount,
        bool retryAttempted = true,
        string? snapshotFailure = null,
        Exception? innerException = null)
        : base(
            BuildMessage(permanentlyLostBlocks, recoveredBlocks, unconcludedBlocks, schedulerEscapedCount, retryAttempted, snapshotFailure),
            innerException)
    {
        PermanentlyLostBlocks = permanentlyLostBlocks;
        RecoveredBlocks = recoveredBlocks;
        UnconcludedBlocks = unconcludedBlocks;
        SchedulerEscapedCount = schedulerEscapedCount;
        SnapshotFailure = snapshotFailure;
    }

    /// <summary>
    /// Blocks whose results were not persisted: the end-of-run retry failed for them, or — on a
    /// breaker abort — the retry was never attempted.
    /// </summary>
    public IReadOnlyList<string> PermanentlyLostBlocks { get; }

    /// <summary>Blocks whose results failed to persist mid-run but were recovered by the end-of-run retry.</summary>
    public IReadOnlyList<string> RecoveredBlocks { get; }

    /// <summary>Blocks that never reached a terminal execution state (NotExecuted or still Running).</summary>
    public IReadOnlyList<string> UnconcludedBlocks { get; }

    /// <summary>Failures that escaped the block runner into the scheduler; their blocks' persisted state is untrusted.</summary>
    public int SchedulerEscapedCount { get; }

    /// <summary>Message of the end-of-run workflow/section state snapshot failure, if that write failed.</summary>
    public string? SnapshotFailure { get; }

    public string FailureKind => WorkflowExecutionFailureKinds.RESULTS_LOST;

    public string FailureDetail => Message;

    private static string BuildMessage(
        IReadOnlyList<string> permanentlyLostBlocks,
        IReadOnlyList<string> recoveredBlocks,
        IReadOnlyList<string> unconcludedBlocks,
        int schedulerEscapedCount,
        bool retryAttempted,
        string? snapshotFailure)
    {
        List<string> parts = ["Workflow execution results were lost or are untrusted; the run must not conclude as finished."];

        BlockNameCategory.AppendTo(
            parts,
            retryAttempted
                ? "permanently lost (persist failed, retry failed)"
                : "lost without retry (persist failed; the breaker declared the store down)",
            permanentlyLostBlocks);
        BlockNameCategory.AppendTo(parts, "recovered on retry (persist failed mid-run, retry succeeded)", recoveredBlocks);
        BlockNameCategory.AppendTo(parts, "never concluded", unconcludedBlocks);

        if (schedulerEscapedCount > 0)
        {
            parts.Add($"{schedulerEscapedCount} block failure(s) escaped the runner (persisted state untrusted).");
        }

        if (snapshotFailure is not null)
        {
            parts.Add($"Workflow/section state snapshot failed: {snapshotFailure}");
        }

        return string.Join(" ", parts);
    }
}
