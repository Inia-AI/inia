using IniaAi.Domain.Entities.Verification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// The outlives-cleanup proof (spec §11): a failure-record row survives the hard-delete of its
/// own execution — the cleanup service nulls the two text columns (feedback, evidence spans)
/// and keeps every identifier, counter and verdict. Text follows the customer; aggregates are
/// permanent. Runs against a throwaway migrated PostgreSQL database; skips without one.
/// </summary>
public class VerificationCriterionRecordCleanupTests : IClassFixture<PostgresUserDataDatabaseFixture>
{
    private const int RETENTION_DAYS = 7;

    private readonly PostgresUserDataDatabaseFixture _fixture;

    public VerificationCriterionRecordCleanupTests(PostgresUserDataDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static VerificationCriterionRecord Row(string organizationId, string executionId, string criterionId)
    {
        return new VerificationCriterionRecord
        {
            OrganizationId = organizationId,
            WorkflowExecutionId = executionId,
            WorkflowTypeId = "corp-part-1",
            TemplateVersionId = "template-version-7",
            TemplateContentHash = "contenthash7",
            VerifierBlockId = "stable-verifier",
            MemberBlockId = "stable-member",
            MemberTag = "income_statement",
            Round = 1,
            RunExecutionId = $"{executionId}_blk_v_r1_1",
            CriterionId = criterionId,
            CriterionLabel = "Totals recompute",
            CriterionDefinitionHash = new string('c', 64),
            Met = "not-met",
            AdvisoryOutcome = "fail-revisable",
            AdvisoryMemberVerdict = "fail",
            ConfiguredRedoMode = "Revise",
            EvidenceSpanCount = 2,
            EvidenceSpans = """[{"Kind":"quote","Text":"Revenue 12"}]""",
            Feedback = "the totals do not recompute",
            EstimatedCostUsd = 0.62m,
            JudgedAtUtc = DateTime.UtcNow,
        };
    }

    [Fact]
    public async Task CleanupAsync_HardDeletingTheExecution_NullsTextAndKeepsEveryAggregate()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(CleanupServiceTestHarness.SKIP_REASON);
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        string org = $"org-{Guid.NewGuid():N}"[..20];
        DateTime pastCutoff = DateTime.UtcNow.AddDays(-(RETENTION_DAYS + 1));

        var purgedExecution = new WorkflowExecution
        {
            OrganizationId = org,
            Title = "purged",
            IsDeleted = true,
            DeletedAt = pastCutoff,
        };
        var survivingExecution = new WorkflowExecution
        {
            OrganizationId = org,
            Title = "surviving",
        };

        await using (UserDataDbContext seed = _fixture.CreateContext())
        {
            seed.WorkflowExecutions.AddRange(purgedExecution, survivingExecution);
            seed.VerificationCriterionRecords.AddRange(
                Row(org, purgedExecution.Id, "totals"),
                Row(org, survivingExecution.Id, "totals"));
            _ = await seed.SaveChangesAsync(ct);
        }

        await using (UserDataDbContext context = _fixture.CreateContext())
        {
            _ = await CleanupServiceTestHarness.CreateService(context).CleanupAsync(org, RETENTION_DAYS, ct);
        }

        await using UserDataDbContext read = _fixture.CreateContext();

        Assert.False(await read.WorkflowExecutions.IgnoreQueryFilters()
            .AnyAsync(execution => execution.Id == purgedExecution.Id, ct));

        VerificationCriterionRecord orphaned = Assert.Single(await read.VerificationCriterionRecords
            .AsNoTracking()
            .Where(row => row.WorkflowExecutionId == purgedExecution.Id)
            .ToListAsync(ct));
        Assert.Null(orphaned.Feedback);
        Assert.Null(orphaned.EvidenceSpans);
        Assert.Equal(org, orphaned.OrganizationId);
        Assert.Equal("corp-part-1", orphaned.WorkflowTypeId);
        Assert.Equal("template-version-7", orphaned.TemplateVersionId);
        Assert.Equal("contenthash7", orphaned.TemplateContentHash);
        Assert.Equal("stable-verifier", orphaned.VerifierBlockId);
        Assert.Equal("stable-member", orphaned.MemberBlockId);
        Assert.Equal("income_statement", orphaned.MemberTag);
        Assert.Equal(1, orphaned.Round);
        Assert.Equal("totals", orphaned.CriterionId);
        Assert.Equal("Totals recompute", orphaned.CriterionLabel);
        Assert.Equal(new string('c', 64), orphaned.CriterionDefinitionHash);
        Assert.Equal("not-met", orphaned.Met);
        Assert.Equal("fail-revisable", orphaned.AdvisoryOutcome);
        Assert.Equal("fail", orphaned.AdvisoryMemberVerdict);
        Assert.Equal("Revise", orphaned.ConfiguredRedoMode);
        Assert.Equal(2, orphaned.EvidenceSpanCount);
        Assert.Equal(0.62m, orphaned.EstimatedCostUsd);

        VerificationCriterionRecord untouched = Assert.Single(await read.VerificationCriterionRecords
            .AsNoTracking()
            .Where(row => row.WorkflowExecutionId == survivingExecution.Id)
            .ToListAsync(ct));
        Assert.Equal("the totals do not recompute", untouched.Feedback);
        Assert.NotNull(untouched.EvidenceSpans);
    }
}
