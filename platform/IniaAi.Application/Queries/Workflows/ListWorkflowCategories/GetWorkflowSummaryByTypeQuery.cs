using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Models.Dto;

namespace IniaAi.Application.Queries.Workflows.ListWorkflowCategories;

/// <summary>
/// Query to retrieve a workflow summary by workflow type ID.
/// Returns Variables, DocumentClasses, and CounterpartyTypeMappings for the CreateWorkflow page.
/// </summary>
/// <param name="OrganizationId">The organization ID to verify access.</param>
/// <param name="WorkflowTypeId">The workflow type ID to retrieve the workflow for.</param>
public record GetWorkflowSummaryByTypeQuery(
    string OrganizationId,
    string WorkflowTypeId) : IRequest<ErrorOr<WorkflowDto>>;
