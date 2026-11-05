using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Screening;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Pins the retention sweep for backtest rows: a document row keeps its verdict, and the judge's
/// reasoning and excerpt, the customer content, go when the document is hard-deleted. A run row
/// holds no document content and is kept whole.
/// </summary>
public class SoftDeleteCleanupServiceScreeningBacktestTests
{
    private const string ORG_ID = "org-backtest-sweep";
    private const string REASONING = "An annual report for the year, with audited statements.";
    private const string EXCERPT = "Annual Report and Accounts";
    private const string MATCHED_CLASSES = """[{"key":"Annual Report","confidence":"high"}]""";
    private const int RETENTION_DAYS = 7;

    private static readonly DateTime _pastCutoff = DateTime.UtcNow.AddDays(-(RETENTION_DAYS + 1));
    private static readonly byte[] _bytes = [0x25, 0x50, 0x44, 0x46];

    [Fact]
    public async Task DocumentSoftDeletedPastCutoff_NullsTheReasoningAndExcerpt_AndKeepsTheVerdict()
    {
        using SqliteCleanupServiceHarness harness = await NewHarnessAsync();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Document purged = SeedDocument(harness.Context, deletedAt: _pastCutoff);
        ScreeningBacktestDocument row = SeedRow(harness.Context, purged.Id);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.CleanupAsync(ORG_ID, RETENTION_DAYS, ct);

        Assert.Empty(result.Errors);
        ScreeningBacktestDocument swept = await LoadAsync(harness, row.Id, ct);
        Assert.Null(swept.Reasoning);
        Assert.Null(swept.EvidenceExcerpt);
        Assert.NotNull(swept.ContentClearedAt);
        Assert.Equal(MATCHED_CLASSES, swept.MatchedClassesJson);
        Assert.Equal(ScreeningBacktestBucket.Agreed, swept.Bucket);
        Assert.Equal(["Annual Report"], swept.UserClasses());
        Assert.Equal(purged.Id, swept.DocumentId);
    }

    [Fact]
    public async Task LiveDocument_KeepsTheReasoning()
    {
        using SqliteCleanupServiceHarness harness = await NewHarnessAsync();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Document live = SeedDocument(harness.Context, deletedAt: null);
        ScreeningBacktestDocument row = SeedRow(harness.Context, live.Id);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.CleanupAsync(ORG_ID, RETENTION_DAYS, ct);

        Assert.Empty(result.Errors);
        ScreeningBacktestDocument kept = await LoadAsync(harness, row.Id, ct);
        Assert.Equal(REASONING, kept.Reasoning);
        Assert.Equal(EXCERPT, kept.EvidenceExcerpt);
        Assert.Null(kept.ContentClearedAt);
    }

    private static async Task<SqliteCleanupServiceHarness> NewHarnessAsync()
    {
        var harness = new SqliteCleanupServiceHarness();
        _ = harness.Context.Organizations.Add(new Organization("Backtest org", ORG_ID));
        _ = await harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        harness.Context.ChangeTracker.Clear();
        return harness;
    }

    private static Document SeedDocument(UserDataDbContext context, DateTime? deletedAt)
    {
        var fileMetadata = new FileMetadata(ORG_ID, "annual-report.pdf", _bytes);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf)
        {
            Name = "annual-report",
            IsDeleted = deletedAt is not null,
            DeletedAt = deletedAt,
        };
        _ = context.FileMetadata.Add(fileMetadata);
        _ = context.Documents.Add(document);
        return document;
    }

    private static ScreeningBacktestDocument SeedRow(UserDataDbContext context, string documentId)
    {
        var row = new ScreeningBacktestDocument
        {
            BacktestId = Guid.NewGuid().ToString(),
            OrganizationId = ORG_ID,
            DocumentId = documentId,
            WorkflowExecutionId = Guid.NewGuid().ToString(),
            FileName = "annual-report.pdf",
            UserClassesJson = ScreeningJsonList.Write(["Annual Report"]),
            IsJudged = true,
            Readable = true,
            MatchedClassesJson = MATCHED_CLASSES,
            BestMatch = "Annual Report",
            Bucket = ScreeningBacktestBucket.Agreed,
            Reasoning = REASONING,
            EvidenceExcerpt = EXCERPT,
        };
        _ = context.ScreeningBacktestDocuments.Add(row);
        return row;
    }

    private static Task<ScreeningBacktestDocument> LoadAsync(SqliteCleanupServiceHarness harness, string rowId, CancellationToken ct)
    {
        return harness.Context.ScreeningBacktestDocuments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(r => r.Id == rowId, ct);
    }
}
