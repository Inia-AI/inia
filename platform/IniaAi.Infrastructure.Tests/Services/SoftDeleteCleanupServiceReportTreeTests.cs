using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Pins the report-tree scoping in <see cref="SoftDeleteCleanupService"/>: a report
/// is purged when its execution is being deleted OR when the report itself was
/// soft-deleted past the retention cutoff, and never leaks across organizations.
/// </summary>
public class SoftDeleteCleanupServiceReportTreeTests : IDisposable
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const int RETENTION_DAYS = 7;

    private readonly DbContextOptions<UserDataDbContext> _options;

    public SoftDeleteCleanupServiceReportTreeTests()
    {
        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseInMemoryDatabase($"SoftDeleteCleanupServiceReportTreeTests-{Guid.NewGuid()}")
            .Options;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DeletedReportQuery_IncludesByExecutionAndSelfDeleted_ExcludesOtherOrgAndLive()
    {
        DateTime pastCutoff = DateTime.UtcNow.AddDays(-(RETENTION_DAYS + 1));
        DateTime cutoff = DateTime.UtcNow.AddDays(-RETENTION_DAYS);

        var deletedExecution = new WorkflowExecution
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = ORG_A,
            IsDeleted = true,
            DeletedAt = pastCutoff,
        };

        var reportOfDeletedExecution = new WorkflowReport
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = ORG_A,
            WorkflowExecutionId = deletedExecution.Id,
            IsDeleted = false,
        };

        var selfDeletedReport = new WorkflowReport
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = ORG_A,
            IsDeleted = true,
            DeletedAt = pastCutoff,
        };

        var otherOrgReport = new WorkflowReport
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = ORG_B,
            IsDeleted = true,
            DeletedAt = pastCutoff,
        };

        var liveReport = new WorkflowReport
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = ORG_A,
            IsDeleted = false,
        };

        await using (var seed = new UserDataDbContext(_options))
        {
            _ = await seed.WorkflowExecutions.AddAsync(deletedExecution, TestContext.Current.CancellationToken);
            await seed.WorkflowReports.AddRangeAsync(
                reportOfDeletedExecution, selfDeletedReport, otherOrgReport, liveReport);
            _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = new UserDataDbContext(_options);
        IQueryable<string> deletedExecutionIds = ctx.WorkflowExecutions.IgnoreQueryFilters()
            .Where(we => we.IsDeleted && we.OrganizationId == ORG_A && we.DeletedAt <= cutoff)
            .Select(we => we.Id);

        List<string> result = await SoftDeleteCleanupService
            .BuildDeletedReportIdsQuery(ctx.WorkflowReports, deletedExecutionIds, ORG_A, cutoff)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains(reportOfDeletedExecution.Id, result);
        Assert.Contains(selfDeletedReport.Id, result);
        Assert.DoesNotContain(otherOrgReport.Id, result);
        Assert.DoesNotContain(liveReport.Id, result);
    }

    [Fact]
    public async Task DeletedReportQuery_ExcludesReportDeletedBeforeCutoffBoundary()
    {
        DateTime preCutoff = DateTime.UtcNow.AddDays(-(RETENTION_DAYS - 1));
        DateTime cutoff = DateTime.UtcNow.AddDays(-RETENTION_DAYS);

        var freshlyDeletedReport = new WorkflowReport
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = ORG_A,
            IsDeleted = true,
            DeletedAt = preCutoff,
        };

        await using (var seed = new UserDataDbContext(_options))
        {
            _ = await seed.WorkflowReports.AddAsync(freshlyDeletedReport, TestContext.Current.CancellationToken);
            _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = new UserDataDbContext(_options);
        IQueryable<string> noDeletedExecutions = ctx.WorkflowExecutions.IgnoreQueryFilters()
            .Where(we => we.IsDeleted && we.OrganizationId == ORG_A && we.DeletedAt <= cutoff)
            .Select(we => we.Id);

        List<string> result = await SoftDeleteCleanupService
            .BuildDeletedReportIdsQuery(ctx.WorkflowReports, noDeletedExecutions, ORG_A, cutoff)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(freshlyDeletedReport.Id, result);
    }
}
