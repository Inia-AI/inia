using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.TokenUsage.GetOrganizationUsageDashboard;

/// <summary>
/// Handles the query to retrieve organization-wide token usage dashboard.
/// </summary>
public class GetOrganizationUsageDashboardQueryHandler
    : IRequestHandler<GetOrganizationUsageDashboardQuery, ErrorOr<OrganizationUsageDashboardDto>>
{
    private readonly IUserDataDbContext _userDataDbContext;
    private readonly ICoreDbContext _coreDbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetOrganizationUsageDashboardQueryHandler"/> class.
    /// </summary>
    public GetOrganizationUsageDashboardQueryHandler(IUserDataDbContext userDataDbContext, ICoreDbContext coreDbContext)
    {
        _userDataDbContext = userDataDbContext;
        _coreDbContext = coreDbContext;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<OrganizationUsageDashboardDto>> Handle(
        GetOrganizationUsageDashboardQuery request,
        CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        DateOnly firstTrendDay = DateOnly.FromDateTime(now.Date.AddDays(-30));

        // Get organization name
        var organization = await _userDataDbContext.Organizations
            .Where(o => o.Id == request.OrganizationId)
            .Select(o => new { o.Name })
            .FirstOrDefaultAsync(cancellationToken);

        IQueryable<UsageDaySlice> slices = TokenUsageSlices.ByDay(_userDataDbContext, request.OrganizationId);
        List<UsageDayTypeSums> byDayAndType = await UsageSummaries.ByDayAndTypeAsync(slices, cancellationToken);
        UsagePeriodSummaries periods = UsageSummaries.Periods(byDayAndType, now);
        List<UsageByTypeDto> byUsageType = UsageSummaries.ByUsageType(byDayAndType);
        long totalTokens = byUsageType.Sum(b => b.TotalTokens);

        var byWorkflowTypeAndUser = await slices
            .GroupBy(s => new { s.WorkflowTypeId, s.UserId })
            .Select(g => new
            {
                g.Key.WorkflowTypeId,
                g.Key.UserId,
                TotalTokens = g.Sum(s => s.InputTokens + s.OutputTokens),
                TotalCost = g.Sum(s => s.Cost),
                OperationCount = g.Sum(s => s.OperationCount),
            })
            .ToListAsync(cancellationToken);

        var byWorkflowType = byWorkflowTypeAndUser
            .Where(s => s.WorkflowTypeId != null)
            .GroupBy(s => s.WorkflowTypeId!)
            .Select(g => new { WorkflowTypeId = g.Key, TotalTokens = g.Sum(s => s.TotalTokens), TotalCost = g.Sum(s => s.TotalCost) })
            .OrderByDescending(w => w.TotalTokens)
            .Take(10)
            .ToList();

        var workflowTypeIds = byWorkflowType.Select(w => w.WorkflowTypeId).ToList();
        Dictionary<string, int> executionCounts = await TokenUsageSlices.ByExecution(_userDataDbContext, request.OrganizationId)
            .Where(e => e.WorkflowTypeId != null && workflowTypeIds.Contains(e.WorkflowTypeId))
            .PerExecution()
            .GroupBy(e => e.WorkflowTypeId!)
            .Select(g => new { WorkflowTypeId = g.Key, ExecutionCount = g.Count() })
            .ToDictionaryAsync(e => e.WorkflowTypeId, e => e.ExecutionCount, cancellationToken);

        // Get workflow type names from CoreDbContext
        Dictionary<string, string> workflowTypeNames = await _coreDbContext.WorkflowTypes
            .Where(wt => workflowTypeIds.Contains(wt.Id))
            .Select(wt => new { wt.Id, wt.Name })
            .ToDictionaryAsync(wt => wt.Id, wt => wt.Name, cancellationToken);

        var byUser = byWorkflowTypeAndUser
            .Where(s => s.UserId != null)
            .GroupBy(s => s.UserId!)
            .Select(g => new
            {
                UserId = g.Key,
                TotalTokens = g.Sum(s => s.TotalTokens),
                TotalCost = g.Sum(s => s.TotalCost),
                OperationCount = g.Sum(s => s.OperationCount),
            })
            .OrderByDescending(u => u.TotalTokens)
            .Take(10)
            .ToList();

        // Get user display names
        var userIds = byUser.Select(u => u.UserId).ToList();
        var userNamesRaw = await _userDataDbContext.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);
        var userNames = userNamesRaw.ToDictionary(
            u => u.Id,
            u => $"{u.FirstName} {u.LastName}".Trim());

        List<DailyUsageTrendDto> dailyTrend = [.. byDayAndType
            .Where(d => d.Day >= firstTrendDay)
            .GroupBy(d => d.Day)
            .OrderBy(g => g.Key)
            .Select(g => new DailyUsageTrendDto
            {
                Date = g.Key,
                TotalTokens = g.Sum(d => d.Tokens),
                TotalCost = g.Sum(d => d.Cost),
                LlmTokens = g.Where(d => d.IsLlm).Sum(d => d.Tokens),
                EmbeddingTokens = g.Where(d => d.IsEmbedding).Sum(d => d.EmbeddingTokens),
                OperationCount = g.Sum(d => d.OperationCount),
            })];

        return new OrganizationUsageDashboardDto
        {
            OrganizationId = request.OrganizationId,
            OrganizationName = organization?.Name ?? "Unknown",
            Today = periods.Today,
            ThisWeek = periods.ThisWeek,
            ThisMonth = periods.ThisMonth,
            AllTime = periods.AllTime,
            ByUsageType = byUsageType,
            ByWorkflowType = [.. byWorkflowType.Select(w => new UsageByWorkflowTypeDto
            {
                WorkflowTypeId = w.WorkflowTypeId,
                WorkflowTypeName = workflowTypeNames.TryGetValue(w.WorkflowTypeId, out string? name) ? name : "Unknown",
                TotalTokens = w.TotalTokens,
                TotalCost = w.TotalCost,
                Percentage = UsageSummaries.Percentage(w.TotalTokens, totalTokens),
                ExecutionCount = executionCounts.GetValueOrDefault(w.WorkflowTypeId),
            })],
            ByUser = [.. byUser.Select(u => new UsageByUserDto
            {
                UserId = u.UserId,
                UserDisplayName = userNames.TryGetValue(u.UserId, out string? displayName) ? displayName : null,
                TotalTokens = u.TotalTokens,
                TotalCost = u.TotalCost,
                Percentage = UsageSummaries.Percentage(u.TotalTokens, totalTokens),
                OperationCount = u.OperationCount,
            })],
            DailyTrend = dailyTrend,
        };
    }
}
