using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.WorkflowTemplates.DuplicateWorkflowType;

public record DuplicateWorkflowTypeCommand(
    string OrganizationId,
    string UserId,
    string SourceWorkflowTypeId,
    string NewWorkflowTypeId,
    string NewName,
    string? NewDescription = null,
    string? NewCategoryId = null) : IRequest<ErrorOr<DuplicateWorkflowTypeResult>>;

public record DuplicateWorkflowTypeResult(
    string NewWorkflowTypeId,
    string VersionId,
    string ContentHash,
    IReadOnlyList<DuplicateSkippedCarrier> SkippedCarriers);

public record DuplicateSkippedCarrier(string Carrier, string Reason);
