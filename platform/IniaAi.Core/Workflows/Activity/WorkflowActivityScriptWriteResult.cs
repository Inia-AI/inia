namespace IniaAi.Core.Workflows.Activity;

/// <summary>
/// The activity script pass's answer, split by what the caller should do next: a written script
/// is stored, a decline is cached on the version so later submissions stop paying for a model
/// call that would answer the same way, and a transient failure leaves the version untouched so
/// the next submission retries.
/// </summary>
public sealed record WorkflowActivityScriptWriteResult(
    WorkflowActivityScriptWriteOutcome Outcome,
    WorkflowActivityScript? Script)
{
    public static readonly WorkflowActivityScriptWriteResult Declined =
        new(WorkflowActivityScriptWriteOutcome.Declined, null);

    public static readonly WorkflowActivityScriptWriteResult TransientFailure =
        new(WorkflowActivityScriptWriteOutcome.TransientFailure, null);

    public static WorkflowActivityScriptWriteResult Written(WorkflowActivityScript script)
    {
        return new(WorkflowActivityScriptWriteOutcome.Written, script);
    }
}

public enum WorkflowActivityScriptWriteOutcome
{
    Written,
    Declined,
    TransientFailure,
}
