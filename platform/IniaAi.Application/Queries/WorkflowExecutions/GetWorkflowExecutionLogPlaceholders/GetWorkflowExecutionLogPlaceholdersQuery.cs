using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecutionLogPlaceholders;

/// <summary>
/// Query to retrieve the full (non-truncated) placeholder values for a specific workflow execution log.
/// Returns placeholders for the specified log.
/// If PlaceholderKeys is provided, only returns values for those specific placeholder keys.
/// </summary>
public record GetWorkflowExecutionLogPlaceholdersQuery(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId,
    string LogId,
    IEnumerable<string>? PlaceholderKeys = null)
    : IRequest<ErrorOr<Dictionary<string, string>>>;
