using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Text;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowTemplates.DeleteWorkflowType;

public class DeleteWorkflowTypeCommandHandler : IRequestHandler<DeleteWorkflowTypeCommand, ErrorOr<Deleted>>
{
    private readonly IWorkflowTypeRetirement _retirement;
    private readonly ILogger<DeleteWorkflowTypeCommandHandler> _logger;

    public DeleteWorkflowTypeCommandHandler(IWorkflowTypeRetirement retirement, ILogger<DeleteWorkflowTypeCommandHandler> logger)
    {
        _retirement = retirement ?? throw new ArgumentNullException(nameof(retirement));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<Deleted>> Handle(DeleteWorkflowTypeCommand request, CancellationToken cancellationToken)
    {
        ErrorOr<Deleted> result = await _retirement.RetireAsync(request.OrganizationId, request.UserId, request.WorkflowTypeId, cancellationToken);
        if (!result.IsError)
        {
            _logger.LogInformation(
                "Workflow type {WorkflowTypeId} deleted by user {UserId} in organization {OrganizationId}",
                LogSanitizer.ForLog(request.WorkflowTypeId),
                request.UserId,
                request.OrganizationId);
        }

        return result;
    }
}
