using IniaAi.Domain.Replay;

namespace IniaAi.Application.Commands.WorkflowExecutions.Shared;

/// <summary>
/// Where a submission's replayed external-fetch outputs come from. A submission has one source or
/// none: the closed hierarchy makes a submission naming two of them impossible to build, so the
/// orchestrator reads one value instead of counting mutually exclusive parameters.
/// </summary>
public abstract record FreezeSource
{
    private FreezeSource()
    {
    }

    /// <summary>
    /// The freeze submission option: a completed execution whose external-fetch outputs this
    /// execution replays. The submission must match it on workflow type, counterparty and variable
    /// fingerprint or the start is refused.
    /// </summary>
    public sealed record Execution(string ExecutionId) : FreezeSource;

    /// <summary>
    /// The freeze submission option that names a validation case. <see cref="ICaseFreezeSources"/>
    /// serves it, and a build without the validation module refuses it.
    /// </summary>
    public sealed record Case(string CaseId) : FreezeSource;

    /// <summary>
    /// The dormant regression path's own replay set, handed over already captured. Uncovered
    /// blocks are skipped as inconclusive here, where the freeze options run them live.
    /// </summary>
    public sealed record ReplaySet(IReadOnlyList<ReplayOutput> Outputs) : FreezeSource;

    /// <summary>
    /// The freeze option as the wire carries it: two optional identifiers, at most one of them set
    /// (<c>SubmitWorkflowExecutionCommandValidator</c> refuses a submission naming both, so a
    /// request naming both is answered 400 rather than reaching here). A blank identifier names no
    /// source. Naming both is a programming error rather than a silent choice between them — a
    /// dropped case id would leave a frozen execution stamped as if no case were involved.
    /// </summary>
    public static FreezeSource? FromSubmissionOptions(string? executionId, string? caseId = null)
    {
        if (!string.IsNullOrWhiteSpace(executionId))
        {
            return string.IsNullOrWhiteSpace(caseId)
                ? new Execution(executionId)
                : throw new InvalidOperationException(
                    "A submission names the freeze source execution or the freeze source case — never both.");
        }

        return string.IsNullOrWhiteSpace(caseId) ? null : new Case(caseId);
    }
}
