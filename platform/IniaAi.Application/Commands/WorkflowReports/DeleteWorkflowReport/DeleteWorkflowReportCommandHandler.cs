using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowReports.DeleteWorkflowReport;

/// <summary>
/// Handler for deleting a workflow report.
/// </summary>
public class DeleteWorkflowReportCommandHandler : IRequestHandler<DeleteWorkflowReportCommand, ErrorOr<Success>>
{
    private readonly ILogger<DeleteWorkflowReportCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataDbContext;
    private readonly IJobsService _jobsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteWorkflowReportCommandHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="userDataDbContext">The user data unit of work.</param>
    /// <param name="jobsService">The jobs service.</param>
    public DeleteWorkflowReportCommandHandler(
        ILogger<DeleteWorkflowReportCommandHandler> logger,
        IUserDataUnitOfWork userDataDbContext,
        IJobsService jobsService)
    {
        _logger = logger;
        _userDataDbContext = userDataDbContext;
        _jobsService = jobsService;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> Handle(DeleteWorkflowReportCommand request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting WorkflowReport with Id: {Id}", LogSanitizer.ForLog(request.Id));

        var spec = new WorkflowExecutionByIdAndOrganizationSpec(request.Id, request.OrganizationId);
        WorkflowExecution? workflowExecution = await _userDataDbContext.WorkflowExecutions.GetFirstOrDefaultAsync(spec, cancellationToken);

        if (workflowExecution is null)
        {
            _logger.LogWarning("WorkflowReport with Id: {Id} not found", LogSanitizer.ForLog(request.Id));
            return Error.NotFound(description: "Workflow Report not found.");
        }

        if (workflowExecution.JobId is not null && (workflowExecution.WorkflowState is WorkflowState.Undefined or WorkflowState.Running))
        {
            _logger.LogWarning("Cancelling JobId for WorkflowExecution with ID: {Id}", LogSanitizer.ForLog(request.Id));
            _jobsService.CancelJob(workflowExecution.JobId);
        }

        _userDataDbContext.WorkflowExecutions.Remove(workflowExecution);
        _ = await _userDataDbContext.CompleteAsync(cancellationToken);

        _logger.LogInformation("WorkflowReport with Id: {Id} deleted successfully", LogSanitizer.ForLog(request.Id));

        return Result.Success;
    }
}
