using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Core.Documents;

/// <summary>A run its content check refused: the job fails the run with the refusal's kind and detail.</summary>
public sealed class RunContentRefusedException(RunContentRefusal refusal) : Exception(refusal.FailureDetail), IWorkflowExecutionFailure
{
    public string FailureKind => refusal.FailureKind;

    public string FailureDetail => refusal.FailureDetail;
}
