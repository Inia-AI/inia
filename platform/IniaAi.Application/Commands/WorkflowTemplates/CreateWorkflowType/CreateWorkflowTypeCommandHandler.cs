using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Common.Text;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowTemplates.CreateWorkflowType;

/// <summary>
/// Handles <see cref="CreateWorkflowTypeCommand"/>, the admin app's Add Workflow Type. The type is
/// saved with the creator's organization access in one unit of work, the same access a template save
/// grants when it creates a type, so the creator's analysts can run it.
/// </summary>
public class CreateWorkflowTypeCommandHandler
    : IRequestHandler<CreateWorkflowTypeCommand, ErrorOr<Created>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowTemplateSavePreflight _preflight;
    private readonly ILogger<CreateWorkflowTypeCommandHandler> _logger;

    public CreateWorkflowTypeCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        WorkflowTemplateSavePreflight preflight,
        ILogger<CreateWorkflowTypeCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<Created>> Handle(CreateWorkflowTypeCommand request, CancellationToken cancellationToken)
    {
        WorkflowTypePresence presence = await _preflight.ReadPresenceAsync(request.WorkflowTypeId, cancellationToken);
        if (presence == WorkflowTypePresence.Live)
        {
            return WorkflowTypeErrorFactory.AlreadyExists(request.WorkflowTypeId);
        }

        if (presence == WorkflowTypePresence.Retired)
        {
            return WorkflowTypeErrorFactory.IdHeldByDeleted(request.WorkflowTypeId);
        }

        if (request.CategoryId is not null && !await _preflight.CategoryExistsAsync(request.CategoryId, cancellationToken))
        {
            return Error.Validation(
                nameof(CreateWorkflowTypeCommand.CategoryId),
                $"The workflow type category '{request.CategoryId}' does not exist. Choose a category from the list.");
        }

        var workflowType = new WorkflowType(request.WorkflowTypeId, request.Name)
        {
            Description = request.Description,
            CategoryId = request.CategoryId,
            CounterpartyTypeMappings = [.. request.CounterpartyTypeIds.Select(counterpartyTypeId => new WorkflowTypeCounterpartyTypeMap
            {
                WorkflowTypeId = request.WorkflowTypeId,
                CounterpartyTypeId = counterpartyTypeId,
            })],
        };
        await _coreUnitOfWork.WorkflowTypes.AddAsync(workflowType);
        await _coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>().AddAsync(new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = request.WorkflowTypeId,
            OrganizationId = request.OrganizationId,
        });
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "User {UserId} created workflow type {WorkflowTypeId} for organization {OrganizationId}",
            request.UserId,
            LogSanitizer.ForLog(request.WorkflowTypeId),
            request.OrganizationId);

        return Result.Created;
    }
}
