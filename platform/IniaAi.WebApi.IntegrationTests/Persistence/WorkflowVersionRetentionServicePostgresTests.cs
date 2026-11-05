using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Runtime proof that <see cref="WorkflowVersionRetentionService"/> clears old payloads on a real
/// PostgreSQL database while leaving the lineage every cross-instance comparison reads completely
/// intact — coverage the InMemory provider cannot give because it has no bytea storage and rejects
/// ExecuteUpdate. The load-bearing assertion is that the ordered content-hash lineage is byte-for-byte
/// identical before and after a trim. Trims are deployment-global, so each test resets the table
/// before seeding its own workflow types.
/// </summary>
public class WorkflowVersionRetentionServicePostgresTests : IClassFixture<PostgresCoreDatabaseFixture>
{
    private const string SKIP_REASON = "No local PostgreSQL reachable (set INIA_TEST_PG to enable).";

    private readonly PostgresCoreDatabaseFixture _fixture;

    public WorkflowVersionRetentionServicePostgresTests(PostgresCoreDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TrimAsync_ClearsOldPayloads_KeepsHeadEarliestRecent_AndStampsTrimmedAt()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(SKIP_REASON);
        }

        const string type = "WT-Clear";
        await ResetVersionsAsync();
        await SeedTenAgedVersionsAsync(type);

        PayloadTrimResult result = await RunTrimAsync(reportOnly: false);

        Assert.False(result.WasReportOnly);
        Assert.Equal(4, result.PayloadsTrimmed);

        await using CoreDbContext verify = _fixture.CreateContext();
        List<WorkflowVersion> ordered = await verify.WorkflowVersions
            .Where(version => version.WorkflowTypeId == type)
            .OrderBy(version => version.CreatedAt)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, ordered.Count(version => version.PayloadTrimmedAt is not null));
        Assert.All(
            ordered.Where(version => version.PayloadTrimmedAt is not null),
            version => Assert.Null(version.WorkflowJsonCompressed));
        Assert.NotNull(ordered[0].WorkflowJsonCompressed);
        Assert.Null(ordered[0].PayloadTrimmedAt);
        Assert.NotNull(ordered[^1].WorkflowJsonCompressed);
        Assert.Null(ordered[^1].PayloadTrimmedAt);
    }

    [Fact]
    public async Task TrimAsync_LeavesOrderedLineageHashesUnchanged()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(SKIP_REASON);
        }

        const string type = "WT-Lineage";
        await ResetVersionsAsync();
        await SeedTenAgedVersionsAsync(type);

        List<string> before = await LineageHashesAsync(type);

        _ = await RunTrimAsync(reportOnly: false);

        List<string> after = await LineageHashesAsync(type);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task TrimAsync_ReportOnly_ChangesNothing_ButReportsCountAndBytes()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(SKIP_REASON);
        }

        const string type = "WT-Report";
        await ResetVersionsAsync();
        await SeedTenAgedVersionsAsync(type);

        PayloadTrimResult result = await RunTrimAsync(reportOnly: true);

        Assert.True(result.WasReportOnly);
        Assert.Equal(4, result.PayloadsTrimmed);
        Assert.True(result.BytesReclaimedEstimate > 0, "byte estimate must be measured from the real payloads");

        await using CoreDbContext verify = _fixture.CreateContext();
        List<WorkflowVersion> all = await verify.WorkflowVersions
            .Where(version => version.WorkflowTypeId == type)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.All(all, version => Assert.Null(version.PayloadTrimmedAt));
        Assert.All(all, version => Assert.NotNull(version.WorkflowJsonCompressed));
    }

    [Fact]
    public async Task TrimAsync_SecondRun_IsIdempotent()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(SKIP_REASON);
        }

        const string type = "WT-Idempotent";
        await ResetVersionsAsync();
        await SeedTenAgedVersionsAsync(type);

        PayloadTrimResult first = await RunTrimAsync(reportOnly: false);
        Assert.Equal(4, first.PayloadsTrimmed);

        await using CoreDbContext afterFirst = _fixture.CreateContext();
        Dictionary<string, DateTime?> stampsAfterFirst = await afterFirst.WorkflowVersions
            .Where(version => version.WorkflowTypeId == type)
            .ToDictionaryAsync(
                version => version.Id,
                version => version.PayloadTrimmedAt,
                TestContext.Current.CancellationToken);

        PayloadTrimResult second = await RunTrimAsync(reportOnly: false);

        Assert.Equal(0, second.PayloadsTrimmed);
        Assert.Equal(0, second.BytesReclaimedEstimate);

        await using CoreDbContext afterSecond = _fixture.CreateContext();
        List<WorkflowVersion> ordered = await afterSecond.WorkflowVersions
            .Where(version => version.WorkflowTypeId == type)
            .OrderBy(version => version.CreatedAt)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, ordered.Count(version => version.PayloadTrimmedAt is not null));
        Assert.All(ordered, version => Assert.Equal(stampsAfterFirst[version.Id], version.PayloadTrimmedAt));
        Assert.NotNull(ordered[0].WorkflowJsonCompressed);
        Assert.NotNull(ordered[^1].WorkflowJsonCompressed);
    }

    [Fact]
    public async Task TrimAsync_OnePassCoversAllTypes()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(SKIP_REASON);
        }

        const string typeA = "WT-Multi-A";
        const string typeB = "WT-Multi-B";
        await ResetVersionsAsync();
        await SeedTenAgedVersionsAsync(typeA);
        await SeedTenAgedVersionsAsync(typeB);

        PayloadTrimResult result = await RunTrimAsync(reportOnly: false);

        Assert.Equal(8, result.PayloadsTrimmed);

        await using CoreDbContext verify = _fixture.CreateContext();
        int trimmedA = await verify.WorkflowVersions
            .CountAsync(
                version => version.WorkflowTypeId == typeA && version.PayloadTrimmedAt != null,
                TestContext.Current.CancellationToken);
        int trimmedB = await verify.WorkflowVersions
            .CountAsync(
                version => version.WorkflowTypeId == typeB && version.PayloadTrimmedAt != null,
                TestContext.Current.CancellationToken);

        Assert.Equal(4, trimmedA);
        Assert.Equal(4, trimmedB);
    }

    private async Task ResetVersionsAsync()
    {
        await using CoreDbContext context = _fixture.CreateContext();
        _ = await context.WorkflowVersions.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private async Task<PayloadTrimResult> RunTrimAsync(bool reportOnly)
    {
        await using CoreDbContext run = _fixture.CreateContext();
        var service = new WorkflowVersionRetentionService(run, NullLogger<WorkflowVersionRetentionService>.Instance);

        return await service.TrimAsync(
            new WorkflowVersionRetentionPolicy(KeepLastNPayloads: 5, KeepNewerThanDays: 1, reportOnly, MaxPayloadsTrimmedPerRun: 500),
            TestContext.Current.CancellationToken);
    }

    private async Task<List<string>> LineageHashesAsync(string workflowTypeId)
    {
        await using CoreDbContext context = _fixture.CreateContext();

        return await context.WorkflowVersions
            .Where(version => version.WorkflowTypeId == workflowTypeId)
            .OrderBy(version => version.CreatedAt)
            .ThenBy(version => version.ContentHash)
            .Select(version => version.ContentHash)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedTenAgedVersionsAsync(string workflowTypeId)
    {
        string payload = "{\"x\":\"" + new string('a', 20000) + "\"}";
        DateTime now = DateTime.UtcNow;

        await using CoreDbContext seed = _fixture.CreateContext();
        for (int i = 0; i < 10; i++)
        {
            _ = seed.WorkflowVersions.Add(new WorkflowVersion
            {
                WorkflowTypeId = workflowTypeId,
                ContentHash = new string((char)('a' + i), 64),
                WorkflowJson = payload,
                AutoNarrativeSummary = "change",
                CreatedByUserId = "user-seed",
                CreatedAt = now.AddDays(-(100 - (i * 10))),
            });
        }

        _ = await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
