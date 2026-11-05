using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.Commands.WorkflowTemplates.UpdateDocumentClass;

public record UpdateDocumentClassCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string Key,
    string? DisplayName,
    DocumentClassRequirement? Requirement,
    bool? ShouldBeSingular,
    bool? IsOutputDocument,
    string? Description,
    string? DocumentCategoryId,
    string? BaseVersionId,
    string? LlmDescription = null) : IRequest<ErrorOr<DocumentClassMutationResult>>;
