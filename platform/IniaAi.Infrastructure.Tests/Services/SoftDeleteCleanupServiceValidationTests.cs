using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Validation;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Pins the retention sweep for the validation entities: runs of a purged case go first
/// (the one real FK in the validation model), then the case itself, then suites.
/// Reference audit logs are append-only by design and are deliberately not swept.
/// </summary>
public class SoftDeleteCleanupServiceValidationTests
{
    private const string ORG = "org-validation-cleanup";
    private const int RETENTION_DAYS = 7;

    private static readonly DateTime _pastCutoff = DateTime.UtcNow.AddDays(-(RETENTION_DAYS + 1));

    [Fact]
    public async Task SoftDeletedCasePastCutoff_DeletesTheCaseAndItsRuns()
    {
        using var harness = new SqliteCleanupServiceHarness();
        CancellationToken ct = TestContext.Current.CancellationToken;

        ValidationCase deletedCase = NewCase(isDeleted: true);
        ValidationCase liveCase = NewCase(isDeleted: false);
        harness.Context.ValidationCases.AddRange(deletedCase, liveCase);
        harness.Context.ValidationRuns.AddRange(
            NewRun(deletedCase.Id),
            NewRun(deletedCase.Id),
            NewRun(liveCase.Id));
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.CleanupAsync(ORG, RETENTION_DAYS, ct);

        Assert.Empty(result.Errors);
        Assert.Equal(0, await CountCasesAsync(harness, deletedCase.Id, ct));
        Assert.Equal(1, await CountCasesAsync(harness, liveCase.Id, ct));
        Assert.Equal(0, await CountRunsOfCaseAsync(harness, deletedCase.Id, ct));
        Assert.Equal(1, await CountRunsOfCaseAsync(harness, liveCase.Id, ct));
    }

    [Fact]
    public async Task SoftDeletedRunUnderALiveCase_IsDeletedAlone()
    {
        using var harness = new SqliteCleanupServiceHarness();
        CancellationToken ct = TestContext.Current.CancellationToken;

        ValidationCase liveCase = NewCase(isDeleted: false);
        ValidationRun deletedRun = NewRun(liveCase.Id, isDeleted: true);
        ValidationRun liveRun = NewRun(liveCase.Id);
        harness.Context.ValidationCases.Add(liveCase);
        harness.Context.ValidationRuns.AddRange(deletedRun, liveRun);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.CleanupAsync(ORG, RETENTION_DAYS, ct);

        Assert.Empty(result.Errors);
        Assert.Equal(1, await CountCasesAsync(harness, liveCase.Id, ct));
        Assert.Equal(1, await CountRunsOfCaseAsync(harness, liveCase.Id, ct));
        Assert.Equal(0, await harness.Context.ValidationRuns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(r => r.Id == deletedRun.Id, ct));
    }

    [Fact]
    public async Task SoftDeletedSuitePastCutoff_IsDeleted()
    {
        using var harness = new SqliteCleanupServiceHarness();
        CancellationToken ct = TestContext.Current.CancellationToken;

        var deletedSuite = new ValidationSuite
        {
            OrganizationId = ORG,
            Name = "stale suite",
            IsDeleted = true,
            DeletedAt = _pastCutoff,
        };
        var liveSuite = new ValidationSuite { OrganizationId = ORG, Name = "live suite" };
        harness.Context.ValidationSuites.AddRange(deletedSuite, liveSuite);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.CleanupAsync(ORG, RETENTION_DAYS, ct);

        Assert.Empty(result.Errors);
        List<string> remainingSuiteIds = await harness.Context.ValidationSuites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(s => s.Id)
            .ToListAsync(ct);
        Assert.Equal([liveSuite.Id], remainingSuiteIds);
    }

    [Fact]
    public async Task SoftDeletedCaseWithinRetention_IsKept()
    {
        using var harness = new SqliteCleanupServiceHarness();
        CancellationToken ct = TestContext.Current.CancellationToken;

        var recentlyDeletedCase = new ValidationCase
        {
            OrganizationId = ORG,
            Name = "recently removed",
            SchemaId = Guid.NewGuid().ToString(),
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow.AddDays(-1),
        };
        harness.Context.ValidationCases.Add(recentlyDeletedCase);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.CleanupAsync(ORG, RETENTION_DAYS, ct);

        Assert.Empty(result.Errors);
        Assert.Equal(1, await CountCasesAsync(harness, recentlyDeletedCase.Id, ct));
    }

    private static ValidationCase NewCase(bool isDeleted)
    {
        return new ValidationCase
        {
            OrganizationId = ORG,
            Name = isDeleted ? "purged case" : "live case",
            SchemaId = Guid.NewGuid().ToString(),
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? _pastCutoff : null,
        };
    }

    private static ValidationRun NewRun(string caseId, bool isDeleted = false)
    {
        return new ValidationRun
        {
            OrganizationId = ORG,
            CaseId = caseId,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? _pastCutoff : null,
        };
    }

    private static Task<int> CountCasesAsync(SqliteCleanupServiceHarness harness, string caseId, CancellationToken ct)
    {
        return harness.Context.ValidationCases
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(c => c.Id == caseId, ct);
    }

    private static Task<int> CountRunsOfCaseAsync(SqliteCleanupServiceHarness harness, string caseId, CancellationToken ct)
    {
        return harness.Context.ValidationRuns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(r => r.CaseId == caseId, ct);
    }
}
