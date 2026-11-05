using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

namespace IniaAi.Application.Queries.WorkflowTemplates.ListWorkflowTemplates;

/// <summary>
/// Lists every workflow template type in the deployment, excluding internal special types.
/// </summary>
/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: templates are authored
/// deployment-wide, and the EDITOR_ROLES gate on the API mouth is the authoring boundary.
/// OrganizationId stays in the contract but does not filter. Org access scopes which
/// templates an organization can consume, not which ones editors can list.
/// </remarks>
public record ListWorkflowTemplatesQuery(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<List<WorkflowTemplateSummaryDto>>>;
