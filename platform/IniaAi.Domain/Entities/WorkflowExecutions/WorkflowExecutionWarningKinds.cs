namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// The vocabulary of <see cref="WorkflowExecution.WarningKind"/>: why a run that completed
/// normally still needs the analyst's attention. Warnings never change the execution's state —
/// the run is Draft or Confirmed as usual — they change how the completed run is presented.
/// </summary>
public static class WorkflowExecutionWarningKinds
{
    /// <summary>At least one verification group released its work unverified — the pass cap was spent with criteria still failing, or the verifier machinery could not run.</summary>
    public const string UNVERIFIED_RELEASE = "UnverifiedRelease";
}
