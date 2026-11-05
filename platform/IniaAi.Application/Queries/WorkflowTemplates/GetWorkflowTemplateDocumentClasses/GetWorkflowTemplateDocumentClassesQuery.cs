using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;

namespace IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateDocumentClasses;

public record GetWorkflowTemplateDocumentClassesQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId) : IRequest<ErrorOr<DocumentClassListResult>>;

public record DocumentClassListResult(
    string WorkflowTypeId,
    string? VersionId,
    IReadOnlyList<DocumentClassView> DocumentClasses);
