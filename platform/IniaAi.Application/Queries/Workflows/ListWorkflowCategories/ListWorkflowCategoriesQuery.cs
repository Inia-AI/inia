using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.Queries.Workflows.ListWorkflowCategories;

/// <summary>
/// Query to retrieve the workflows.
/// </summary>
/// <param name="OrganizationId">The organization ID to filter workflow categories by.</param>
/// <param name="UserId">The user ID making the request.</param>
/// <param name="IncludeWorkflows">Whether to include workflows in the response.</param>
public record ListWorkflowCategoriesQuery(
    string OrganizationId,
    string UserId,
    bool IncludeWorkflows = false) : IRequest<ErrorOr<List<WorkflowCategoryDto>>>;
