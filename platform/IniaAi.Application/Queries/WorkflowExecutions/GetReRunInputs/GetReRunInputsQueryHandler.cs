using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Workflows.Interfaces;
using IniaAi.Common.Text;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.WorkflowExecutions;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetReRunInputs;

/// <summary>
/// Handles <see cref="GetReRunInputsQuery"/>: reads the run's inputs and the workflow it executed
/// from UserData, the current template from Core, and plans the re-run with
/// <see cref="ReRunInputsPlanner"/>.
/// </summary>
public sealed class GetReRunInputsQueryHandler : IRequestHandler<GetReRunInputsQuery, ErrorOr<ReRunInputsDto>>
{
    private const string WORKFLOW_NOT_AVAILABLE_CODE = "WorkflowExecution.ReRunWorkflowNotAvailable";
    private const string EXECUTION_NOT_FOUND = "Workflow Execution not found.";

    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ReRunInputsPlanner _planner;
    private readonly ILogger<GetReRunInputsQueryHandler> _logger;

    public GetReRunInputsQueryHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        IWorkflowVariableValidationService variableValidation,
        IDocumentClassValidationService documentClassValidation,
        ILogger<GetReRunInputsQueryHandler> logger)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
        _planner = new ReRunInputsPlanner(variableValidation, documentClassValidation);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<ReRunInputsDto>> Handle(GetReRunInputsQuery request, CancellationToken cancellationToken)
    {
        WorkflowExecution? execution = await _userDataUnitOfWork
            .Repository<WorkflowExecution>()
            .GetFirstOrDefaultAsync(
                new WorkflowExecutionByIdAndOrganizationSpec(request.WorkflowExecutionId, request.OrganizationId),
                cancellationToken);

        if (execution is null)
        {
            _logger.LogWarning("Re-run inputs requested for unknown execution {ExecutionId}", LogSanitizer.ForLog(request.WorkflowExecutionId));
            return Error.NotFound(description: EXECUTION_NOT_FOUND);
        }

        if (string.IsNullOrEmpty(execution.WorkflowTypeId)
            || !Workflow.GetAvailability(execution.WorkflowTypeId)
            || !await WorkflowTypeAccessGuard.HasAccessAsync(_coreUnitOfWork, request.OrganizationId, execution.WorkflowTypeId, cancellationToken))
        {
            return WorkflowNotAvailable();
        }

        Workflow? template = await _coreUnitOfWork
            .Repository<Workflow>()
            .GetFirstOrDefaultAsync(new WorkflowTemplateInputsByTypeSpec(execution.WorkflowTypeId), cancellationToken);

        if (template is null)
        {
            return WorkflowNotAvailable();
        }

        List<WorkflowVariable> runVariables = string.IsNullOrEmpty(execution.WorkflowId)
            ? []
            : [.. await _userDataUnitOfWork
                .Repository<WorkflowVariable>()
                .GetAllAsync(new WorkflowVariablesByWorkflowIdSpec(execution.WorkflowId), cancellationToken)];
        HashSet<string> runOutputClassKeys = string.IsNullOrEmpty(execution.WorkflowId)
            ? []
            : [.. await _userDataUnitOfWork
                .Repository<DocumentClass>()
                .GetAllAsync(new OutputDocumentClassKeysByWorkflowIdSpec(execution.WorkflowId), cancellationToken)];

        List<RunInputDocument> inputDocuments = string.IsNullOrEmpty(execution.JobId)
            ? []
            : [.. (await _userDataUnitOfWork
                .Repository<DocumentInfo>()
                .GetAllAsync(new RunInputDocumentsByJobIdSpec(execution.JobId, request.OrganizationId), cancellationToken))
                .Where(document => !runOutputClassKeys.Contains(document.DocumentClass))
                .DistinctBy(document => document.DocumentId)];

        return _planner.Plan(template, runVariables, execution.Variables, inputDocuments);
    }

    private static Error WorkflowNotAvailable()
    {
        return Error.NotFound(
            WORKFLOW_NOT_AVAILABLE_CODE,
            "The workflow this report ran is no longer available to your organization, so the report cannot be re-run.");
    }
}
