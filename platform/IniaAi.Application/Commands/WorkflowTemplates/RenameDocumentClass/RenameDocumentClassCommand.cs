using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.WorkflowTemplates.RenameDocumentClass;

public record RenameDocumentClassCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string Key,
    string NewKey,
    string? BaseVersionId) : IRequest<ErrorOr<DocumentClassMutationResult>>;
