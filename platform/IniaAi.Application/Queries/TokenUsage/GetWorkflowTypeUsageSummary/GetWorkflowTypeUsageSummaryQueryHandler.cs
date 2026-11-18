using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.TokenUsage.GetWorkflowTypeUsageSummary;

/// <summary>
/// Handles the query to retrieve token usage summary for a specific workflow type.
/// </summary>
public class GetWorkflowTypeUsageSummaryQueryHandler
    : IRequestHandler<GetWorkflowTypeUsageSummaryQuery, ErrorOr<WorkflowTypeUsageSummaryDto>>
{
    private readonly IUserDataDbContext _userDataDbContext;
    private readonly ICoreDbContext _coreDbContext;
    private readonly WorkflowTypeAccess _access;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetWorkflowTypeUsageSummaryQueryHandler"/> class.
    /// </summary>
    public GetWorkflowTypeUsageSummaryQueryHandler(IUserDataDbContext userDataDbContext, ICoreDbContext coreDbContext, WorkflowTypeAccess access)
    {
        _userDataDbContext = userDataDbContext;
        _coreDbContext = coreDbContext;
        _access = access;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<WorkflowTypeUsageSummaryDto>> Handle(
        GetWorkflowTypeUsageSummaryQuery request,
        CancellationToken cancellationToken)
    {
        // Get workflow type name from CoreDbContext
        string? workflowTypeName = await _coreDbContext.WorkflowTypes
            .Where(wt => wt.Id == request.WorkflowTypeId)
            .Select(wt => wt.Name)
            .FirstOrDefaultAsync(cancellationToken);

        IQueryable<UsageDaySlice> slices = TokenUsageSlices.ByDay(_userDataDbContext, request.OrganizationId)
            .Where(s => s.WorkflowTypeId == request.WorkflowTypeId);

        // A type the organization lost access to keeps its history; one it never had stays unnamed
        if (workflowTypeName is null
            || (!await _access.CanReachAsync(request.OrganizationId, request.WorkflowTypeId, cancellationToken) && !await slices.AnyAsync(cancellationToken)))
        {
            return Error.NotFound("WorkflowType.NotFound", $"Workflow type {request.WorkflowTypeId} not found.");
        }

        List<UsageDayTypeSums> byDayAndType = await UsageSummaries.ByDayAndTypeAsync(slices, cancellationToken);
        TokenUsageSummaryDto totals = UsageSummaries.Periods(byDayAndType, DateTime.UtcNow).AllTime;
        long embeddingTokens = byDayAndType.Where(d => d.IsEmbedding).Sum(d => d.EmbeddingTokens);

        IQueryable<UsageExecutionSlice> executions = TokenUsageSlices.ByExecution(_userDataDbContext, request.OrganizationId)
            .Where(e => e.WorkflowTypeId == request.WorkflowTypeId)
            .PerExecution();

        // Counts an execution purged since it ran, as the cost total does
        int executionCount = await executions.CountAsync(cancellationToken);

        // Recent executions that still exist, with the user who ran each
        var recentExecutions = await executions
            .Join(
                _userDataDbContext.WorkflowExecutions.IgnoreQueryFilters(),
                e => e.WorkflowExecutionId,
                w => w.Id,
                (e, w) => new { e.WorkflowExecutionId, w.UserId, TotalTokens = e.InputTokens + e.OutputTokens, TotalCost = e.Cost, ExecutedAt = e.FirstUsageAt })
            .OrderByDescending(e => e.ExecutedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        // Get user display names for recent executions
        var userIds = recentExecutions.Where(e => e.UserId is not null).Select(e => e.UserId!).Distinct().ToList();
        var userNamesRaw = await _userDataDbContext.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);
        var userNames = userNamesRaw.ToDictionary(
            u => u.Id,
            u => $"{u.FirstName} {u.LastName}".Trim());

        return new WorkflowTypeUsageSummaryDto
        {
            WorkflowTypeId = request.WorkflowTypeId,
            WorkflowTypeName = workflowTypeName,
            TotalInputTokens = totals.TotalInputTokens,
            TotalOutputTokens = totals.TotalOutputTokens,
            TotalTokens = totals.TotalTokens,
            TotalEmbeddingTokens = embeddingTokens,
            TotalCost = totals.TotalCost,
            ExecutionCount = executionCount,
            AverageTokensPerExecution = executionCount > 0 ? totals.TotalTokens / executionCount : 0,
            AverageCostPerExecution = executionCount > 0 ? totals.TotalCost / executionCount : 0,
            ByUsageType = UsageSummaries.ByUsageType(byDayAndType),
            RecentExecutions = [.. recentExecutions.Select(e => new ExecutionUsageSummaryDto
            {
                WorkflowExecutionId = e.WorkflowExecutionId,
                UserId = e.UserId,
                UserDisplayName = e.UserId is not null && userNames.TryGetValue(e.UserId, out string? name) ? name : null,
                TotalTokens = e.TotalTokens,
                TotalCost = e.TotalCost,
                ExecutedAt = e.ExecutedAt,
            })],
        };
    }
}
