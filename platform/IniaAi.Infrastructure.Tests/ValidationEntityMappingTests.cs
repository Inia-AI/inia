using IniaAi.Domain.Entities.Validation;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Infrastructure.Tests;

/// <summary>
/// Mapping-level guards for the validation entities on a real relational provider:
/// the gzip payload columns and enum scalars survive a save/reload cycle, and the
/// <c>[ConcurrencyCheck]</c> on <see cref="ValidationCase.ReferenceRevision"/> makes
/// a stale concurrent reviewer write fail instead of silently overwriting.
/// </summary>
public class ValidationEntityMappingTests : IDisposable
{
    private const string ORG = "org-validation-mapping";
    private const string BASELINE_TEXT = "NAV as of 2026-06-30: $1,234,567.89";
    private const string SCHEMA_SNAPSHOT_JSON = """{"fields":[{"name":"Nav","type":"number"}]}""";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<UserDataDbContext> _options;

    public ValidationEntityMappingTests()
    {
        _connection = SqliteUserDataSchema.OpenInMemoryDatabase();

        _options = new DbContextOptionsBuilder<UserDataDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task CaseAndRun_SurviveASaveReloadCycleIntact()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        var validationCase = new ValidationCase
        {
            OrganizationId = ORG,
            Name = "Q2 fund report",
            SchemaId = "schema-1",
            BaselineDocumentId = "doc-1",
            BaselineText = BASELINE_TEXT,
            BaselineTextLength = BASELINE_TEXT.Length,
            BaselineTextHash = new string('a', 64),
            ResolvedSchemaJson = SCHEMA_SNAPSHOT_JSON,
            SelectedFieldsJson = """["Nav"]""",
            ReferenceJson = """{"Nav":{"value":"1234567.89"}}""",
            ExtractionFingerprint = new string('b', 64),
            ReferenceModelId = "anthropic.claude-model-v9",
            ReferenceExtractedAt = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc),
            ReferenceRevision = 3,
            SelectedFieldCount = 1,
            ConfirmedFieldCount = 1,
            State = ValidationCaseState.Active,
        };

        var run = new ValidationRun
        {
            OrganizationId = ORG,
            CaseId = validationCase.Id,
            Status = ValidationRunStatus.Completed,
            Outcome = ValidationRunOutcome.NeedsReview,
            ResolvedSchemaJson = SCHEMA_SNAPSHOT_JSON,
            ReferenceSnapshotJson = """{"Nav":"1234567.89"}""",
            CandidateExtractionJson = """{"Nav":"1234567.88"}""",
            VerdictsJson = """{"Nav":0}""",
            MatchRatePercent = 87.5,
            ScoreableFieldCount = 8,
            MatchedFieldCount = 7,
            FreezeReplayedCount = 2,
            FreezeEligibleCount = 3,
            TotalInputTokens = 120_000,
            TotalOutputTokens = 4_500,
            TotalCost = 1.2345678901m,
            CandidateSourceKind = ValidationCandidateSourceKind.Execution,
            CandidateSourceId = "execution-1",
            CandidateWorkflowTypeId = "wf-type-1",
            CandidateExecutedAt = new DateTime(2026, 8, 12, 11, 0, 0, DateTimeKind.Utc),
            ExtractorModelId = "anthropic.claude-model-v9",
            JudgeModelId = "anthropic.claude-model-v9",
            PromptSetHash = new string('c', 64),
            ExtractionFingerprint = new string('b', 64),
            ReferenceRevisionUsed = 3,
            BatchId = "batch-1",
            RepeatIndex = 2,
            SubmittedAt = new DateTime(2026, 8, 12, 11, 0, 0, DateTimeKind.Utc),
            DeadlineAt = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc),
            Accepted = true,
            AcceptedAt = new DateTime(2026, 8, 12, 13, 0, 0, DateTimeKind.Utc),
        };

        await using (var context = new SqliteUserDataDbContext(_options))
        {
            context.ValidationCases.Add(validationCase);
            context.ValidationRuns.Add(run);
            _ = await context.SaveChangesAsync(ct);
        }

        await using (var context = new SqliteUserDataDbContext(_options))
        {
            ValidationCase reloadedCase = await context.ValidationCases.SingleAsync(c => c.Id == validationCase.Id, ct);
            ValidationRun reloadedRun = await context.ValidationRuns.SingleAsync(r => r.Id == run.Id, ct);

            Assert.Equal(BASELINE_TEXT, reloadedCase.BaselineText);
            Assert.Equal(SCHEMA_SNAPSHOT_JSON, reloadedCase.ResolvedSchemaJson);
            Assert.Equal(ValidationCaseState.Active, reloadedCase.State);
            Assert.Equal(3, reloadedCase.ReferenceRevision);
            Assert.Equal(1, reloadedCase.ConfirmedFieldCount);

            Assert.Equal(ValidationRunStatus.Completed, reloadedRun.Status);
            Assert.Equal(ValidationRunOutcome.NeedsReview, reloadedRun.Outcome);
            Assert.Equal(SCHEMA_SNAPSHOT_JSON, reloadedRun.ResolvedSchemaJson);
            Assert.Equal("""{"Nav":"1234567.89"}""", reloadedRun.ReferenceSnapshotJson);
            Assert.Equal("""{"Nav":"1234567.88"}""", reloadedRun.CandidateExtractionJson);
            Assert.Equal(87.5, reloadedRun.MatchRatePercent);
            Assert.Equal(1.2345678901m, reloadedRun.TotalCost);
            Assert.Equal(ValidationCandidateSourceKind.Execution, reloadedRun.CandidateSourceKind);
            Assert.True(reloadedRun.Accepted);
            Assert.Null(reloadedRun.PayloadTrimmedAt);
        }
    }

    [Fact]
    public async Task StaleReferenceRevisionWrite_ThrowsInsteadOfOverwriting()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        var validationCase = new ValidationCase
        {
            OrganizationId = ORG,
            Name = "contended case",
            SchemaId = "schema-1",
        };

        await using (var context = new SqliteUserDataDbContext(_options))
        {
            context.ValidationCases.Add(validationCase);
            _ = await context.SaveChangesAsync(ct);
        }

        await using var firstReviewer = new SqliteUserDataDbContext(_options);
        await using var secondReviewer = new SqliteUserDataDbContext(_options);

        ValidationCase firstView = await firstReviewer.ValidationCases.SingleAsync(c => c.Id == validationCase.Id, ct);
        ValidationCase secondView = await secondReviewer.ValidationCases.SingleAsync(c => c.Id == validationCase.Id, ct);

        firstView.ReferenceRevision++;
        firstView.ConfirmedFieldCount = 5;
        _ = await firstReviewer.SaveChangesAsync(ct);

        secondView.ReferenceRevision++;
        secondView.ConfirmedFieldCount = 9;

        _ = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondReviewer.SaveChangesAsync(ct));
    }
}
