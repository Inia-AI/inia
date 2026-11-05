using ErrorOr;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Queries.ReportFeedback.ListWorkflowTypeFeedback;

/// <summary>
/// One keyset page of every organization's feedback on a workflow type, newest first. This is
/// the one feedback read that crosses the tenant boundary, by design: it serves editors who
/// tune the shared template, so it is SuperAdmin only and each item carries its organization id.
/// An organization Admin can grant WorkflowEditor inside its own organization, so that role
/// cannot guard a read across organizations. The controller gates it, and the handler refuses
/// any caller the controller did not mark as a SuperAdmin. <paramref name="OrganizationId"/> is the caller's own
/// organization, kept for the house convention; it does not narrow the result.
/// </summary>
/// <param name="OrganizationId">The caller's organization.</param>
/// <param name="UserId">The caller.</param>
/// <param name="WorkflowTypeId">The workflow type whose feedback to list.</param>
/// <param name="CallerIsSuperAdmin">Whether the caller holds <c>Roles.SUPER_ADMIN</c>.</param>
/// <param name="Status">Optional status filter.</param>
/// <param name="Take">Page size.</param>
/// <param name="Cursor">The previous page's <c>nextCursor</c>; null for the first page.</param>
public sealed record ListWorkflowTypeFeedbackQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    bool CallerIsSuperAdmin,
    ReportFeedbackStatusDto? Status = null,
    int Take = 50,
    string? Cursor = null) : IRequest<ErrorOr<ReportFeedbackPageDto>>;
