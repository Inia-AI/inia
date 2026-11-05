using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Replay;
using IniaAi.WebApi.Contracts.Requests;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

namespace IniaAi.Application.Commands.WorkflowExecutions.StartWorkflowExecution;

/// <summary>
/// Command to initiate the execution of a specified workflow with supporting files and metadata.
/// </summary>
public class StartWorkflowExecutionCommand : IRequest<ErrorOr<WorkflowExecutionDto>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StartWorkflowExecutionCommand"/> class.
    /// </summary>
    /// <param name="request">The structured request describing the workflow execution parameters.</param>
    /// <param name="organizationId">The ID of the user's organization.</param>
    /// <param name="userId">The ID of the user starting the workflow.</param>
    /// <param name="username">The username of the user starting the workflow.</param>
    /// <param name="email">The email of the user starting the workflow.</param>
    public StartWorkflowExecutionCommand(
        StartWorkflowExecutionRequest request,
        string organizationId,
        string userId,
        string? username,
        string? email)
    {
        Request = request;
        OrganizationId = organizationId;
        UserId = userId;
        Username = username;
        Email = email;
    }

    /// <summary>
    /// Gets the organization ID for the workflow execution.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the ID of the user initiating the workflow.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets the username of the user initiating the workflow.
    /// </summary>
    public string? Username { get; }

    /// <summary>
    /// Gets the email of the user initiating the workflow.
    /// </summary>
    public string? Email { get; }

    /// <summary>
    /// Gets or sets the structured request payload that contains workflow execution details,
    /// such as workflow ID, files, metadata, and flags.
    /// </summary>
    public StartWorkflowExecutionRequest Request { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the workflow should be executed in test mode.
    /// </summary>
    public bool IsTestRun { get; set; }

    /// <summary>
    /// Optional captured external-block outputs to freeze and replay into this execution. Set by
    /// the regression import-and-run handler so external lookups (web search, FX, external API) are
    /// not re-fetched during a rerun. Carried on the command, never on the public request DTO.
    /// </summary>
    public IReadOnlyList<ReplayOutput>? ReplayOutputs { get; set; }

    /// <summary>
    /// True for a regression rerun: the execution saves no change to live data. Carried on the
    /// command, never on the public request DTO.
    /// </summary>
    public bool IsRerun { get; set; }
}
