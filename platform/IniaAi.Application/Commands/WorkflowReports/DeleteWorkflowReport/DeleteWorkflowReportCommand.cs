using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.WorkflowReports.DeleteWorkflowReport;

/// <summary>
/// Command to delete a workflow report
/// </summary>
public class DeleteWorkflowReportCommand : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteWorkflowReportCommand"/> class.
    /// </summary>
    /// <param name="organizationId">The organization ID for tenant isolation.</param>
    /// <param name="userId">The user ID for audit logging.</param>
    /// <param name="id">The workflow report (execution) id.</param>
    public DeleteWorkflowReportCommand(string organizationId, string userId, string id)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Id = id;
    }

    /// <summary>
    /// Gets the Organization ID.
    /// </summary>
    public string OrganizationId { get; }

    /// <summary>
    /// Gets the User ID.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets the Workflow Report Id.
    /// </summary>
    public string Id { get; }
}
