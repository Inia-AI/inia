using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.Commands.WorkflowTemplates.CreateDocumentClass;

public record CreateDocumentClassCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string Key,
    string DisplayName,
    string Description,
    string? DocumentCategoryId,
    DocumentClassRequirement Requirement,
    bool ShouldBeSingular,
    bool IsOutputDocument,
    string? BaseVersionId,
    string? LlmDescription = null) : IRequest<ErrorOr<DocumentClassMutationResult>>;
