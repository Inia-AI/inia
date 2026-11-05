using System.Collections.Concurrent;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Replay;
using IniaAi.Domain.UserData.Interfaces;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Core.Workflows.Interfaces;

public interface IWorkflowHandler
{
    List<Page> AllPages { get; }
    ConcurrentDictionary<string, string> SharedInputs { get; }
    string? CounterpartyId { get; set; }
    List<string>? PeerCompanyIds { get; set; }

    /// <summary>
    /// Gets or sets the organization ID for the current execution context.
    /// Used for token usage tracking and multi-tenant isolation.
    /// </summary>
    string? OrganizationId { get; set; }

    /// <summary>
    /// Gets or sets the user ID for the current execution context.
    /// Used for token usage tracking to attribute usage to specific users.
    /// </summary>
    string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the execution context type for token usage tracking.
    /// Set to BlockTest or SectionTest for test executions, defaults to WorkflowExecution.
    /// </summary>
    TokenExecutionContext ExecutionContext { get; set; }

    /// <summary>
    /// Gets the ID of the root workflow execution created for this handler.
    /// Available after <see cref="CreateRootOutputAsync"/> has been called.
    /// </summary>
    string? WorkflowExecutionId { get; }

    Task<Workflow> ExecuteWorkflowAsync(
        Workflow workflow,
        ConcurrentDictionary<string, string> anonymisationMap,
        string organizationId,
        string jobId,
        IUserDataUnitOfWork userDataUnitOfWork,
        string? userId = null,
        Dictionary<string, string>? inputs = null,
        IReadOnlyList<ReplayOutput>? replayOutputs = null,
        ReplayUncoveredBlockPolicy replayUncoveredBlockPolicy = ReplayUncoveredBlockPolicy.SkipAsInconclusive,
        CancellationToken cancellationToken = default);

    Task ExecuteSectionAsync(
        Section section,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        Dictionary<string, string>? inputs = null,
        WorkflowOutputContext? workflowOutputContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Expands a parameterized section into its variant sections without executing them.
    /// Returns the expanded sections, or an empty list if the section is not parameterized.
    /// The template section is removed from the parent workflow and replaced with the variants.
    /// </summary>
    Task<List<Section>> ExpandParameterizedSectionAsync(
        Section section,
        IUserDataUnitOfWork userDataUnitOfWork);

    Task<string?> ExecuteBlockAsync(
        Section section,
        int index,
        ConcurrentDictionary<string, string> anonymisationMap,
        IUserDataUnitOfWork userDataUnitOfWork,
        WorkflowOutputContext? workflowOutputContext = null,
        Dictionary<string, string>? inputs = null,
        CancellationToken cancellationToken = default);

    Task CreateRootOutputAsync(
        IUserDataUnitOfWork userDataUnitOfWork,
        string jobId,
        Workflow rootWorkflow,
        string organizationId,
        string? userId,
        Dictionary<string, object>? variables = null,
        string? workflowExecutionId = null);

    /// <summary>
    /// Moves the execution to <paramref name="state"/> only while the row is still active.
    /// Throws <see cref="WorkflowExecutionFencedException"/> when the row reached a terminal state
    /// elsewhere — a lost write is never something a caller can drop on the floor, so it unwinds
    /// to the one handler that stops the job instead of returning a flag each site must remember
    /// to check.
    /// </summary>
    Task UpdateWorkflowStateAsync(
        WorkflowState state,
        IUserDataUnitOfWork userDataUnitOfWork);
}
