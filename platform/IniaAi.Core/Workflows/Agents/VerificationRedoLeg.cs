using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Agents;

/// <summary>
/// What one redo leg's dispatch carries through <c>BlockExecutionContext.VerificationRedoLeg</c>:
/// the scratch directive every leg can fall back to, the two mode stamps the run bookkeeping
/// records (D36f, D37i), and — on a leg planned as a continuation — the revise half. The loop
/// composes the directives and plans the mode; the Agent block strategy dispatches and owns
/// only the in-flight degrade, which is <c>leg with { Revise = null, ActualRedoMode = SCRATCH_MODE }</c>.
/// <see cref="ActualRedoMode"/> is the planned mode as dispatched; the one divergence — the
/// executor re-running a failed continuation from scratch mid-call — is stamped per attempt
/// off <c>AgentRunAttempt.RanContinuationFallback</c> and read back from the ledger.
/// </summary>
public sealed record VerificationRedoLeg(
    string ScratchDirective,
    string ConfiguredRedoMode,
    string ActualRedoMode)
{
    public const string REVISE_MODE = nameof(VerificationRedoMode.Revise);
    public const string SCRATCH_MODE = nameof(VerificationRedoMode.Scratch);

    /// <summary>
    /// The revise half: the revision turn delivered as the resumed session's answer, and the
    /// payload that resumes it. Null on a scratch leg — a continuation without its revision
    /// turn, or the reverse, is unrepresentable.
    /// </summary>
    public VerificationReviseLeg? Revise { get; init; }
}

/// <summary>One planned continuation: the revision turn and the session it resumes.</summary>
public sealed record VerificationReviseLeg(string Directive, AgentRunContinuation Continuation);
