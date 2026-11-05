using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.WorkflowTemplates.DeleteDocumentClass;

public record DeleteDocumentClassCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string Key,
    string? BaseVersionId) : IRequest<ErrorOr<DocumentClassMutationResult>>;
