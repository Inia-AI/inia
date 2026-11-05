using ErrorOr;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Core.Workflows.Outputs.Interfaces;

public interface IWorkflowOutputService
{
    Task<ReportSection?> CreateReportSectionAsync(
        Section section,
        WorkflowOutputContext? context,
        IUserDataUnitOfWork userDataUnitOfWork);

    Task<WorkflowExecution> CreateExecutionFromWorkflowAsync(
        Workflow workflow,
        WorkflowOutputContext? context,
        IUserDataUnitOfWork userDataUnitOfWork,
        DateTime? reviewDate = null,
        WorkflowState? workflowState = null,
        string? jobId = null,
        Dictionary<string, object>? variables = null);

    Task<ReportParagraph> CreateReportParagraphAsync(
        ReportParagraph reportParagraph,
        IUserDataUnitOfWork userDataUnitOfWork);

    Task<(ReportParagraph? reportParagraph, string? reason, bool isValidForOutput)> BuildReportParagraphFromBlock(
        Block block,
        WorkflowOutputContext? context,
        BlockAnnotations? blockAnnotations,
        WorkflowAnnotationsContext? annotationsContext = null);

    /// <summary>
    /// The block's data output, or why it has none. Null when the block is not a data output, when
    /// it failed or was skipped, or when a stop marker in its inputs skips its output as it skips
    /// its report paragraph.
    /// </summary>
    ErrorOr<ReportDataOutput>? BuildDataOutputFromBlock(
        Block block,
        WorkflowOutputContext? context,
        BlockAnnotations? blockAnnotations,
        WorkflowAnnotationsContext? annotationsContext = null);

    /// <summary>
    /// Creates a Section → Paragraph pair under the given workflow execution root,
    /// used to surface an error message in the report when a workflow fails before any
    /// output blocks have executed.
    /// </summary>
    Task CreateErrorOutputAsync(
        string workflowExecutionId,
        string errorMessage,
        IUserDataUnitOfWork userDataUnitOfWork);

    /// <summary>
    /// Persists <see cref="WorkflowState.Failed"/> for the execution through a fresh
    /// DI scope, so a poisoned unit of work (e.g. holding an entity PostgreSQL rejects)
    /// can never prevent a dead run from being marked Failed and leave it stuck
    /// "in progress" in the UI. Skips executions already in a terminal state.
    /// Never throws — persistence failures are logged. A supplied kind and detail are recorded on
    /// the row, so the reason survives on the execution itself rather than only in the logs.
    /// Returns whether this call won the flip; false means the row was already terminal,
    /// missing, or the write failed. A won flip also runs agent-run terminal cleanup, so the
    /// runs the dead execution bought are completed or cancelled on the runtime, and then the run
    /// settlement.
    /// </summary>
    Task<bool> MarkExecutionAsFailedIfActiveAsync(
        string? workflowExecutionId,
        string? organizationId,
        string? failureKind = null,
        string? failureDetail = null);

    /// <summary>
    /// Same isolated persistence as <see cref="MarkExecutionAsFailedIfActiveAsync"/> — the
    /// won-flip return, the agent-run cleanup and the settlement included — but transitions to
    /// <see cref="WorkflowState.Cancelled"/> after the running task has observed
    /// cancellation. Skips rows the API path already moved to a terminal state.
    /// </summary>
    Task<bool> MarkExecutionAsCancelledIfActiveAsync(string? workflowExecutionId, string? organizationId);

    /// <summary>
    /// Appends a Failed "Document intake" log row to the execution through a fresh DI scope,
    /// so the reason a job died on a document (e.g. a password-protected file that slipped
    /// past the ingest gate) survives durably instead of living only on the in-memory job.
    /// Idempotent for an identical row and never throws — persistence failures are logged.
    /// </summary>
    Task AppendDocumentIntakeErrorLogAsync(string? workflowExecutionId, string documentName, string error);

    /// <summary>
    /// Records a Failed row on the execution for a page the model refused to read, named by its
    /// document and page and carrying the refusal category. Once per page and execution.
    /// </summary>
    Task AppendPageRefusalLogAsync(string? workflowExecutionId, string documentName, int pageNumber, string refusalCategory, bool wholePage);

    /// <summary>
    /// Appends a Failed "Execution" log row describing lost or unpersisted results, through a
    /// fresh DI scope — the run's own persistence is exactly what failed, so this is the one
    /// channel that can still make the reason visible on the execution. Best-effort and never
    /// throws; persistence failures are logged.
    /// </summary>
    Task AppendExecutionErrorLogAsync(string? workflowExecutionId, string error);

    Task<WorkflowExecution?> GetWorkflowExecution(
        string workflowExecutionId,
        IUserDataUnitOfWork userDataUnitOfWork);
}
