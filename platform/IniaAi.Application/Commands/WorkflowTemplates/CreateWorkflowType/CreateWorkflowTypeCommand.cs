using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.WorkflowTemplates.CreateWorkflowType;

public record CreateWorkflowTypeCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string Name,
    string? Description,
    string? CategoryId,
    IReadOnlyCollection<string> CounterpartyTypeIds) : IRequest<ErrorOr<Created>>;
