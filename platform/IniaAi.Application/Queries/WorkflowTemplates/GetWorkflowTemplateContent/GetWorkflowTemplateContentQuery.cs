using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;

/// <summary>
/// Retrieves the full current workflow template JSON for a workflow type,
/// plus the head version information needed for optimistic concurrency on save.
/// </summary>
/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: templates are authored
/// deployment-wide, and the EDITOR_ROLES gate on the API mouth is the authoring boundary.
/// OrganizationId stays in the contract but does not filter. Org access scopes which
/// templates an organization can consume, not which ones editors can read.
/// </remarks>
public record GetWorkflowTemplateContentQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId) : IRequest<ErrorOr<WorkflowTemplateContentResult>>;

/// <summary>
/// The current template content and head version metadata.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type identifier.</param>
/// <param name="VersionId">The head version identifier, or null when no usable version snapshot exists.</param>
/// <param name="ContentHash">The canonical content hash of the head version, when available.</param>
/// <param name="CreatedAtUtc">When the head version was created, when available.</param>
/// <param name="CreatedByUserId">Who created the head version, when available.</param>
/// <param name="WorkflowJson">The canonical workflow template JSON.</param>
public record WorkflowTemplateContentResult(
    string WorkflowTypeId,
    string? VersionId,
    string? ContentHash,
    DateTime? CreatedAtUtc,
    string? CreatedByUserId,
    string WorkflowJson);
