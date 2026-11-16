namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// The vocabulary of <see cref="WorkflowExecution.FailureKind"/>: a stable, machine-readable
/// classification of why a run ended Failed, so operators can triage without parsing the detail.
/// </summary>
public static class WorkflowExecutionFailureKinds
{
    /// <summary>A block whose failure must not be absorbed failed, so the run was aborted.</summary>
    public const string CRITICAL_BLOCK_FAILURE = "CriticalBlockFailure";

    /// <summary>Block results could not be trusted: a persist failed, a block never concluded, or a failure escaped the runner.</summary>
    public const string RESULTS_LOST = "ResultsLost";

    /// <summary>The execution stayed active past the configured maximum lifetime, so the janitor failed it — the host that was pumping it is presumed dead.</summary>
    public const string LIFETIME_EXCEEDED = "LifetimeExceeded";

    /// <summary>A verification group's shape could not be honored: its members and verifier do not dispatch together, or a cyclic reference forced one of them to run without its inputs.</summary>
    public const string VERIFICATION_GROUP_SHAPE = "VerificationGroupShape";

    /// <summary>The verifier judged the source itself unable to satisfy a criterion — no member revision can change that, so redo rounds would only burn spend. The detail states the criterion and why.</summary>
    public const string VERIFICATION_UNSATISFIABLE = "VerificationUnsatisfiable";

    /// <summary>The process that held the run's job stopped heartbeating, so the run could never finish; the host-loss sweep failed it.</summary>
    public const string HOST_LOST = "HostLost";

    /// <summary>None of the run's documents gave any text or picture to read, so the run stopped before its blocks.</summary>
    public const string NO_READABLE_DOCUMENTS = "NoReadableDocuments";

    /// <summary>The model failed to read the run's documents, so the run stopped before its blocks: a problem on the platform's side, not with the documents.</summary>
    public const string DOCUMENTS_NOT_READ = "DocumentsNotRead";

    /// <summary>A document the run was sent was deleted before the run read it, so the run stopped before its blocks.</summary>
    public const string INPUT_DOCUMENT_DELETED = "InputDocumentDeleted";
}
