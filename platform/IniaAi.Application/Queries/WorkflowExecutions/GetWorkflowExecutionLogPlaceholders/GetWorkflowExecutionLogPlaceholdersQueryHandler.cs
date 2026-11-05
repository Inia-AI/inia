using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Utilities;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecutionLogPlaceholders;

/// <summary>
/// Handles the query to retrieve full placeholder values for workflow execution logs.
/// </summary>
public class GetWorkflowExecutionLogPlaceholdersQueryHandler
    : IRequestHandler<GetWorkflowExecutionLogPlaceholdersQuery, ErrorOr<Dictionary<string, string>>>
{
    private readonly ILogger<GetWorkflowExecutionLogPlaceholdersQueryHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetWorkflowExecutionLogPlaceholdersQueryHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="userDataUnitOfWork">The unit of work for user data repository access.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="userDataUnitOfWork"/> is null.</exception>
    public GetWorkflowExecutionLogPlaceholdersQueryHandler(
        ILogger<GetWorkflowExecutionLogPlaceholdersQueryHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _logger = logger;
    }

    /// <summary>
    /// Handles the incoming request to return full placeholder values for workflow execution logs.
    /// </summary>
    /// <param name="request">The request containing workflow execution ID and optional log ID.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A dictionary of placeholder values for the specified log.</returns>
    public async Task<ErrorOr<Dictionary<string, string>>> Handle(
        GetWorkflowExecutionLogPlaceholdersQuery request, CancellationToken cancellationToken)
    {
        // Create specification for the query
        var specification = new WorkflowExecutionLogsWithBlockSpec(request.WorkflowExecutionId, request.LogId);

        WorkflowExecutionLog? log = await _userDataUnitOfWork.Repository<WorkflowExecutionLog>()
            .GetAll(specification)
            .Where(x => x.WorkflowExecution.OrganizationId == request.OrganizationId)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (log is null)
        {
            return Error.NotFound("WorkflowExecutionLog.NotFound", "No workflow execution log was found for the specified criteria.");
        }

        string template = log.Block?.Type is BlockTypes.AiQuery && log.Block.Properties is AiQueryProperties aiProps
            ? aiProps.UserPromptTemplate ?? string.Empty
            : string.Empty;
        string resolved = WorkflowExecutionLogPromptResolver.GetResolvedPrompt(log);
        Dictionary<string, string> placeholderValues = WorkflowExecutionLogPromptResolver.ExtractPlaceholderValues(template, resolved, log.Id, _logger);

        // Filter by specific placeholder keys if provided
        if (request.PlaceholderKeys is not null && request.PlaceholderKeys.Any())
        {
            placeholderValues = WorkflowExecutionLogPromptResolver.FilterPlaceholderValues(placeholderValues, request.PlaceholderKeys);
        }

        return placeholderValues is null || placeholderValues.Count == 0
            ? (ErrorOr<Dictionary<string, string>>)Error.NotFound("PlaceholderValues.NotFound", "No matching placeholder values were found for the specified keys.")
            : (ErrorOr<Dictionary<string, string>>)placeholderValues;
    }
}
