using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.WebApi.Contracts.Responses.TokenUsage;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.TokenUsage.GetMyUsageSummary;

/// <summary>
/// Handles the query to retrieve personal token usage summary.
/// </summary>
public class GetMyUsageSummaryQueryHandler : IRequestHandler<GetMyUsageSummaryQuery, ErrorOr<MyUsageSummaryDto>>
{
    private readonly IUserDataDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetMyUsageSummaryQueryHandler"/> class.
    /// </summary>
    public GetMyUsageSummaryQueryHandler(IUserDataDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<ErrorOr<MyUsageSummaryDto>> Handle(
        GetMyUsageSummaryQuery request,
        CancellationToken cancellationToken)
    {
        IQueryable<UsageDaySlice> slices = TokenUsageSlices.ByDay(_dbContext, request.OrganizationId)
            .Where(s => s.UserId == request.UserId);
        List<UsageDayTypeSums> byDayAndType = await UsageSummaries.ByDayAndTypeAsync(slices, cancellationToken);
        UsagePeriodSummaries periods = UsageSummaries.Periods(byDayAndType, DateTime.UtcNow);

        // Get user display name
        var user = await _dbContext.Users
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.FirstName, u.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        string? userDisplayName = user is not null
            ? $"{user.FirstName} {user.LastName}".Trim()
            : null;

        return new MyUsageSummaryDto
        {
            UserId = request.UserId,
            UserDisplayName = userDisplayName,
            Today = periods.Today,
            ThisWeek = periods.ThisWeek,
            ThisMonth = periods.ThisMonth,
            AllTime = periods.AllTime,
            ByUsageType = UsageSummaries.ByUsageType(byDayAndType),
        };
    }
}
