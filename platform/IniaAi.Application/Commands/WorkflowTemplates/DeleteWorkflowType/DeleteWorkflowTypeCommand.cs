using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.WorkflowTemplates.DeleteWorkflowType;

public record DeleteWorkflowTypeCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId) : IRequest<ErrorOr<Deleted>>;
