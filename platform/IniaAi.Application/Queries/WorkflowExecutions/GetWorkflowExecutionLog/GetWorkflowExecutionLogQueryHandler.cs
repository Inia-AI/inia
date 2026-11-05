using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Utilities;
using IniaAi.Core.Common.Helpers;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecutionLog;

/// <summary>
/// Handles the query to retrieve a single workflow execution log with full details.
/// Optimized to only load and process what's requested (prompt, output, or both).
/// </summary>
public class GetWorkflowExecutionLogQueryHandler
    : IRequestHandler<GetWorkflowExecutionLogQuery, ErrorOr<WorkflowOutputExecutionLogDto>>
{
    private readonly ILogger<GetWorkflowExecutionLogQueryHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetWorkflowExecutionLogQueryHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="userDataUnitOfWork">The unit of work for user data repository access.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public GetWorkflowExecutionLogQueryHandler(
        ILogger<GetWorkflowExecutionLogQueryHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
    }

    /// <summary>
    /// Handles the incoming request to return a single workflow execution log with selective details.
    /// Only loads and processes what's requested for optimal performance.
    /// </summary>
    /// <param name="request">The request containing the workflow execution ID, log ID, and include flags.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A single workflow execution log DTO with requested content.</returns>
    public async Task<ErrorOr<WorkflowOutputExecutionLogDto>> Handle(
        GetWorkflowExecutionLogQuery request,
        CancellationToken cancellationToken)
    {
        // Use projection to only load the fields we need from the database
        // Security: Filter by OrganizationId to prevent cross-tenant access
        // Note: EF Core may still reference columns in SQL via CASE WHEN for conditional projections.
        // For full column exclusion, query shape would need to branch based on flags (future optimization).
        var logData = await _userDataUnitOfWork
            .Repository<WorkflowExecutionLog>()
            .GetAll()
            .Where(x =>
                x.Id == request.LogId &&
                x.WorkflowExecutionId == request.WorkflowExecutionId &&
                x.WorkflowExecution != null &&
                x.WorkflowExecution.OrganizationId == request.OrganizationId)
            .AsNoTracking()
            .Select(log => new
            {
                log.Id,
                log.Inputs,

                // Prompt fields - only load if requested
                // UserPromptTemplatePreview is for display, UserPromptTemplate is for placeholder extraction
                Prompt = request.IncludePrompt && log.Block != null
                    ? log.Block.TryGetProperties<AiQueryProperties>() != null
                        ? log.Block.TryGetProperties<AiQueryProperties>()!.UserPromptTemplatePreview
                        : null
                    : null,
                PromptTemplate = request.IncludePrompt && log.Block != null
                    ? log.Block.TryGetProperties<AiQueryProperties>() != null
                        ? log.Block.TryGetProperties<AiQueryProperties>()!.UserPromptTemplate
                        : null
                    : null,

                // ExecutionState fields for placeholder resolution
                // UserPrompt contains the resolved prompt with placeholders already substituted
                // InputContext may need to be substituted into UserPrompt
                ExecutionStateUserPrompt = request.IncludePrompt && log.Block != null
                    ? log.Block.TryGetProperties<AiQueryProperties>() != null && log.Block.TryGetProperties<AiQueryProperties>()!.ExecutionState != null
                        ? log.Block.TryGetProperties<AiQueryProperties>()!.ExecutionState!.UserPrompt
                        : null
                    : null,
                ExecutionStateInputContext = request.IncludePrompt && log.Block != null
                    ? log.Block.TryGetProperties<AiQueryProperties>() != null && log.Block.TryGetProperties<AiQueryProperties>()!.ExecutionState != null
                        ? log.Block.TryGetProperties<AiQueryProperties>()!.ExecutionState!.InputContext
                        : null
                    : null,

                // Fallback prompt from WorkflowExecutionLog itself (used if ExecutionState.UserPrompt is null)
                LogPrompt = request.IncludePrompt ? log.Prompt : null,

                // Output field - only load if requested
                // Note: EF Core translates nested null checks to SQL CASE WHEN with proper NULL handling.
                // RawResponse may be null even when BlockResponse exists; null is a valid return value here.
                Output = request.IncludeOutput && log.Block != null && log.Block.BlockResponse != null
                    ? log.Block.BlockResponse.RawResponse : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (logData is null)
        {
            return Error.NotFound(
                "WorkflowExecutionLog.NotFound",
                $"Workflow execution log with ID '{request.LogId}' was not found for workflow execution '{request.WorkflowExecutionId}'.");
        }

        // Only do expensive placeholder extraction if prompt is requested
        Dictionary<string, PlaceholderValueDto>? enrichedValues = null;
        if (request.IncludePrompt && !string.IsNullOrEmpty(logData.PromptTemplate))
        {
            // Get resolved prompt from ExecutionState (contains already-substituted placeholders)
            // This mirrors the logic in WorkflowExecutionLogPromptResolver.GetResolvedPrompt
            // Fallback chain: ExecutionState.UserPrompt (with InputContext) -> log.Prompt -> empty
            string resolvedPrompt = GetResolvedPromptFromExecutionState(
                logData.ExecutionStateUserPrompt,
                logData.ExecutionStateInputContext,
                logData.LogPrompt);

            enrichedValues = WorkflowExecutionLogPromptResolver.ExtractEnrichedPlaceholderValues(
                logData.PromptTemplate, resolvedPrompt, logData.Id, _logger);
        }

        var result = new WorkflowOutputExecutionLogDto
        {
            Id = logData.Id,
            Inputs = logData.Inputs ?? [],
            Prompt = logData.Prompt,
            Output = logData.Output,
            PromptPlaceholderValues = enrichedValues,
        };

        return result;
    }

    /// <summary>
    /// Gets the resolved prompt from ExecutionState data, handling InputContext replacement and fallbacks.
    /// </summary>
    /// <remarks>
    /// This mirrors the logic in WorkflowExecutionLogPromptResolver.GetResolvedPrompt.
    /// Fallback chain: ExecutionState.UserPrompt (with InputContext substitution) -> log.Prompt -> empty string.
    /// ExecutionState.UserPrompt contains the prompt with placeholders already substituted during execution.
    /// InputContext may need to be substituted into UserPrompt if present.
    /// </remarks>
    /// <param name="userPrompt">The ExecutionState.UserPrompt value (already has placeholders resolved).</param>
    /// <param name="inputContext">The ExecutionState.InputContext value (for InputContext substitution).</param>
    /// <param name="logPrompt">The WorkflowExecutionLog.Prompt value (fallback if ExecutionState.UserPrompt is null).</param>
    /// <returns>The fully resolved prompt string.</returns>
    private static string GetResolvedPromptFromExecutionState(string? userPrompt, string? inputContext, string? logPrompt)
    {
        // If UserPrompt is available, use it (with InputContext substitution if needed)
        if (!string.IsNullOrEmpty(userPrompt))
        {
            // If both InputContext and UserPrompt are available, substitute InputContext into UserPrompt
            return !string.IsNullOrEmpty(inputContext)
                ? userPrompt.Replace("{InputContext}", inputContext, StringComparison.OrdinalIgnoreCase)
                : userPrompt;
        }

        // Fallback to log.Prompt if ExecutionState.UserPrompt is not available
        return logPrompt ?? string.Empty;
    }
}
