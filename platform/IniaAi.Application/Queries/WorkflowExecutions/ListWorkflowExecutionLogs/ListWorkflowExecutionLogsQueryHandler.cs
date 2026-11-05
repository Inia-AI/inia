using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Core.Common.Helpers;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.EntityFrameworkCore;
using BlockTypes = IniaAi.Domain.Entities.Workflows.Enums.BlockTypes;

namespace IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutionLogs;

/// <summary>
/// Handles the query to retrieve a lightweight summary list of workflow execution logs.
/// Returns only essential fields for list display - use GetWorkflowExecutionLogQuery for full details.
/// </summary>
public class ListWorkflowExecutionLogsQueryHandler
    : IRequestHandler<ListWorkflowExecutionLogsQuery, ErrorOr<List<WorkflowExecutionLogSummaryDto>>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListWorkflowExecutionLogsQueryHandler"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">The unit of work for user data repository access.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="userDataUnitOfWork"/> is null.</exception>
    public ListWorkflowExecutionLogsQueryHandler(IUserDataUnitOfWork userDataUnitOfWork)
    {
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
    }

    /// <summary>
    /// Handles the incoming request to return a lightweight list of workflow execution logs.
    /// </summary>
    /// <param name="request">The request containing the workflow execution ID.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A list of workflow execution log summary DTOs.</returns>
    public async Task<ErrorOr<List<WorkflowExecutionLogSummaryDto>>> Handle(
        ListWorkflowExecutionLogsQuery request,
        CancellationToken cancellationToken)
    {
        // Use EF Core projection to select only the columns we need
        // This generates efficient SQL without loading full Block/BlockResponse entities
        // Security: Filter by OrganizationId to prevent cross-tenant access
        List<WorkflowExecutionLogSummaryDto> result = await _userDataUnitOfWork
            .Repository<WorkflowExecutionLog>()
            .GetAll()
            .Where(log =>
                log.WorkflowExecutionId == request.Id &&
                log.WorkflowExecution != null &&
                log.WorkflowExecution.OrganizationId == request.OrganizationId)
            .AsNoTracking()
            // Authored (sequential) order, not parallel completion order: section order, then block
            // order, with CreatedAt as a stable tie-break (e.g. loop iterations of the same block).
            // Explicit null handling pins block-less rows (section skips) last on both providers.
            .OrderBy(log => log.Block != null && log.Block.Section != null ? log.Block.Section.Order : int.MaxValue)
            .ThenBy(log => log.Block != null ? log.Block.Order : int.MaxValue)
            .ThenBy(log => log.CreatedAt)
            .Select(log => new WorkflowExecutionLogSummaryDto
            {
                Id = log.Id,
                SectionName = log.SectionName,
                BlockName = log.BlockName,
                BlockType = log.BlockType,

                // Prefer Block's ReplacementTag if available, fallback to log's own value
                ReplacementTag = log.Block != null ? log.Block.ReplacementTag : log.ReplacementTag,
                ModelUsed = log.ModelUsed,
                Status = (BlockExecutionStatus)log.Status,
                Error = log.Error,
                RefusalCategory = log.RefusalCategory,
                CreatedAt = log.CreatedAt,
                ShouldDisplayInLogs = log.Block == null || log.Block.ShouldDisplayInLogs,

                // Only check if content exists - EF Core translates this to efficient SQL
                // Note: Empty strings are treated as having content; UI decides whether to show toggle
                HasPrompt = log.Block != null
                    && log.Block.Type == BlockTypes.AiQuery
                    && log.Block.TryGetProperties<AiQueryProperties>() != null
                    && log.Block.TryGetProperties<AiQueryProperties>()!.UserPromptTemplate != null,
                HasOutput = log.Block != null && log.Block.BlockResponse != null && log.Block.BlockResponse.RawResponse != null,
                HasInputs = log.Inputs != null, // Can't use .Count on JSONB - detail view will show actual content
            })
            .ToListAsync(cancellationToken);

        return result;
    }
}
