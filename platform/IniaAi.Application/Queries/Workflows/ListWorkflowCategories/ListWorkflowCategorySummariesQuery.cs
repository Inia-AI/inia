using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.Queries.Workflows.ListWorkflowCategories;

/// <summary>
/// Query to retrieve lightweight workflow category summaries.
/// Returns only category names and basic workflow info (no Variables, DocumentClasses, etc.).
/// </summary>
/// <param name="OrganizationId">The organization ID to filter workflow categories by.</param>
/// <param name="UserId">The user whose chosen workflow types narrow the list, if they have chosen any.</param>
public record ListWorkflowCategorySummariesQuery(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<List<WorkflowCategorySummaryDto>>>;
