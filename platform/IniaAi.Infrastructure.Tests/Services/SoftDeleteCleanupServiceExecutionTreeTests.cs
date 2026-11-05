using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Infrastructure.Tests.Helpers;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Tests.Services;

/// <summary>
/// Pins the execution-tree purge the validation retention trim runs: everything beneath a named
/// execution goes — the clone with its blocks, responses, inputs, settings, classes, variables and
/// fields, the logs, the report tree, the counterparties link, the job's document-info rows, the
/// output documents with their pages, embeddings, metadata and bytes, and the agent-run
/// evidence hanging off the blocks — while the input document the job shared with another job,
/// the token usage rows, the counterparty, and a neighbouring execution's tree stay exactly as
/// they were. Every "gone" here is a read that came back empty on a real relational provider.
/// </summary>
public class SoftDeleteCleanupServiceExecutionTreeTests
{
    private const string ORG_A = "org-a";
    private const string ORG_B = "org-b";
    private const string JOB = "job-purged";
    private const string OTHER_JOB = "job-neighbour";
    private const string OUTPUT_CLASS = "Report";
    private const string INPUT_CLASS = "Statement";

    private static readonly byte[] _bytes = [1, 2, 3];

    [Fact]
    public async Task PurgeExecutionTrees_RemovesTheWholeTree_AndLeavesSharedInputsNeighboursAndUsageAlone()
    {
        using var harness = new TreeFixture();
        CancellationToken ct = TestContext.Current.CancellationToken;

        SeededTree purged = SeedTree(harness, ORG_A, JOB, withAgentEvidence: true);
        SeededTree neighbour = SeedTree(harness, ORG_A, OTHER_JOB, withAgentEvidence: false);
        Document sharedInput = SeedDocument(harness, ORG_A, "statement.pdf", StorageProvider.Database, out _);
        harness.Context.DocumentInfos.AddRange(
            new DocumentInfo(sharedInput.Id) { JobId = JOB, DocumentClass = INPUT_CLASS },
            new DocumentInfo(sharedInput.Id) { JobId = OTHER_JOB, DocumentClass = INPUT_CLASS });
        var usage = new TokenUsage
        {
            OrganizationId = ORG_A,
            WorkflowExecutionId = purged.Execution.Id,
            DocumentId = purged.OutputDocument.Id,
            ModelId = "model",
            Provider = "provider",
            Family = "family",
        };
        _ = harness.Context.TokenUsages.Add(usage);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.PurgeExecutionTreesAsync(
            ORG_A,
            [purged.Execution.Id],
            ct);

        Assert.Empty(result.Errors);
        await AssertTreeGoneAsync(harness, purged, ct);
        await AssertTreeIntactAsync(harness, neighbour, ct);

        Assert.Equal(1, await harness.Context.Documents.IgnoreQueryFilters().CountAsync(d => d.Id == sharedInput.Id, ct));
        Assert.Equal(1, await harness.Context.Pages.IgnoreQueryFilters().CountAsync(p => p.DocumentId == sharedInput.Id, ct));
        Assert.Equal(1, await harness.Context.FileMetadata.IgnoreQueryFilters().CountAsync(fm => fm.Id == sharedInput.FileMetadataId, ct));
        List<string?> sharedInputJobs = await harness.Context.DocumentInfos
            .Where(di => di.DocumentId == sharedInput.Id)
            .Select(di => di.JobId)
            .ToListAsync(ct);
        Assert.Equal([OTHER_JOB], sharedInputJobs);

        TokenUsage keptUsage = await harness.Context.TokenUsages.IgnoreQueryFilters().SingleAsync(t => t.Id == usage.Id, ct);
        Assert.Null(keptUsage.WorkflowExecutionId);
        Assert.Null(keptUsage.DocumentId);
        Assert.Equal(1, await harness.Context.Counterparties.IgnoreQueryFilters().CountAsync(c => c.Id == purged.Counterparty.Id, ct));
    }

    [Fact]
    public async Task PurgeExecutionTrees_ReleasesExternalBytesAfterTheCommit()
    {
        using var harness = new TreeFixture();
        CancellationToken ct = TestContext.Current.CancellationToken;

        SeededTree purged = SeedTree(harness, ORG_A, JOB, withAgentEvidence: false, outputProvider: StorageProvider.S3);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();
        string key = purged.OutputDocument.FileMetadata!.ExternalStoragePath!;
        Assert.Equal(_bytes, harness.Storage.GetTestFile(ORG_A, StorageCategories.Documents, key));

        CleanupResult result = await harness.Service.PurgeExecutionTreesAsync(
            ORG_A,
            [purged.Execution.Id],
            ct);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.FilesDeleted);
        Assert.Null(harness.Storage.GetTestFile(ORG_A, StorageCategories.Documents, key));
        Assert.Equal(0, await harness.Context.FileMetadata.IgnoreQueryFilters().CountAsync(fm => fm.Id == purged.OutputDocument.FileMetadataId, ct));
    }

    [Fact]
    public async Task PurgeExecutionTrees_RootsOfAnotherOrganization_AreLeftAlone()
    {
        using var harness = new TreeFixture();
        CancellationToken ct = TestContext.Current.CancellationToken;

        SeededTree theirs = SeedTree(harness, ORG_B, JOB, withAgentEvidence: false);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.PurgeExecutionTreesAsync(
            ORG_A,
            [theirs.Execution.Id],
            ct);

        Assert.Empty(result.Errors);
        Assert.Equal(0, result.TotalDeleted);
        await AssertTreeIntactAsync(harness, theirs, ct);
    }

    [Fact]
    public async Task PurgeExecutionTrees_DropsTheTreesOwnVectors_AndLeavesAStrayOrphanToTheRetentionCleanup()
    {
        using var harness = new TreeFixture();
        CancellationToken ct = TestContext.Current.CancellationToken;

        SeededTree purged = SeedTree(harness, ORG_A, JOB, withAgentEvidence: false);
        var stray = new EmbeddingVector("model", [0.3]);
        _ = harness.Context.EmbeddingVectors.Add(stray);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult trimmed = await harness.Service.PurgeExecutionTreesAsync(ORG_A, [purged.Execution.Id], ct);

        Assert.Empty(trimmed.Errors);
        Assert.Equal(0, await CountVectorsAsync(harness, [purged.PageEmbedding.Id], ct));
        Assert.Equal(1, await CountVectorsAsync(harness, [stray.Id], ct));

        CleanupResult swept = await harness.Service.CleanupAsync(ORG_A, retentionDays: 30, ct);

        Assert.Empty(swept.Errors);
        Assert.Equal(0, await CountVectorsAsync(harness, [stray.Id], ct));
    }

    [Fact]
    public async Task PurgeExecutionTrees_NoRoots_DeletesNothing()
    {
        using var harness = new TreeFixture();
        CancellationToken ct = TestContext.Current.CancellationToken;

        SeededTree tree = SeedTree(harness, ORG_A, JOB, withAgentEvidence: true);
        _ = await harness.Context.SaveChangesAsync(ct);
        harness.Context.ChangeTracker.Clear();

        CleanupResult result = await harness.Service.PurgeExecutionTreesAsync(ORG_A, [], ct);

        Assert.Empty(result.Errors);
        Assert.Equal(0, result.TotalDeleted);
        await AssertTreeIntactAsync(harness, tree, ct);
    }

    private static async Task AssertTreeGoneAsync(TreeFixture harness, SeededTree tree, CancellationToken ct)
    {
        UserDataDbContext db = harness.Context;
        Assert.Equal(0, await db.WorkflowExecutions.IgnoreQueryFilters().CountAsync(e => e.Id == tree.Execution.Id, ct));
        Assert.Equal(0, await db.Workflows.IgnoreQueryFilters().CountAsync(w => w.Id == tree.ExecutedWorkflow.Id, ct));
        Assert.Equal(0, await db.Sections.IgnoreQueryFilters().CountAsync(s => s.WorkflowId == tree.ExecutedWorkflow.Id, ct));
        Assert.Equal(0, await db.Blocks.IgnoreQueryFilters().CountAsync(b => b.Id == tree.Block.Id, ct));
        Assert.Equal(0, await db.BlockResponses.IgnoreQueryFilters().CountAsync(r => r.BlockId == tree.Block.Id, ct));
        Assert.Equal(0, await db.TextInputs.IgnoreQueryFilters().CountAsync(t => t.BlockId == tree.Block.Id, ct));
        Assert.Equal(0, await db.RagSettings.IgnoreQueryFilters().CountAsync(r => r.BlockId == tree.Block.Id, ct));
        Assert.Equal(0, await db.Set<DocumentClass>().IgnoreQueryFilters().CountAsync(dc => dc.WorkflowId == tree.ExecutedWorkflow.Id, ct));
        Assert.Equal(0, await db.Set<WorkflowVariable>().IgnoreQueryFilters().CountAsync(v => v.WorkflowId == tree.ExecutedWorkflow.Id, ct));
        Assert.Equal(0, await db.Set<WorkflowRegressionField>().IgnoreQueryFilters().CountAsync(f => f.WorkflowId == tree.ExecutedWorkflow.Id, ct));
        Assert.Equal(0, await db.Set<WorkflowSignatureField>().IgnoreQueryFilters().CountAsync(f => f.WorkflowId == tree.ExecutedWorkflow.Id, ct));
        Assert.Equal(0, await db.WorkflowExecutionLogs.IgnoreQueryFilters().CountAsync(l => l.WorkflowExecutionId == tree.Execution.Id, ct));
        Assert.Equal(0, await db.WorkflowExecutionCounterparties.IgnoreQueryFilters().CountAsync(c => c.WorkflowExecutionId == tree.Execution.Id, ct));
        Assert.Equal(0, await db.WorkflowReports.IgnoreQueryFilters().CountAsync(r => r.WorkflowExecutionId == tree.Execution.Id, ct));
        Assert.Equal(0, await db.ReportSections.IgnoreQueryFilters().CountAsync(s => s.WorkflowReportId == tree.Report.Id, ct));
        Assert.Equal(0, await db.ReportParagraphs.IgnoreQueryFilters().CountAsync(p => p.Id == tree.Paragraph.Id, ct));
        Assert.Equal(0, await db.DocumentInfos.IgnoreQueryFilters().CountAsync(di => di.JobId == tree.Execution.JobId, ct));
        Assert.Equal(0, await db.Documents.IgnoreQueryFilters().CountAsync(d => d.Id == tree.OutputDocument.Id, ct));
        Assert.Equal(0, await db.Pages.IgnoreQueryFilters().CountAsync(p => p.DocumentId == tree.OutputDocument.Id, ct));
        Assert.Equal(0, await CountVectorsAsync(harness, [tree.PageEmbedding.Id], ct));
        Assert.Equal(0, await db.FileMetadata.IgnoreQueryFilters().CountAsync(fm => fm.Id == tree.OutputDocument.FileMetadataId, ct));
        Assert.Equal(0, await db.Files.IgnoreQueryFilters().CountAsync(f => f.Id == tree.OutputBlob.Id, ct));
        Assert.Equal(0, await db.AgentRunOutputs.IgnoreQueryFilters().CountAsync(o => o.BlockId == tree.Block.Id, ct));
        Assert.Equal(0, await db.AgentRunTraceFiles.IgnoreQueryFilters().CountAsync(t => t.BlockId == tree.Block.Id, ct));
        Assert.Equal(0, await db.BlockCitationTraces.IgnoreQueryFilters().CountAsync(t => t.BlockId == tree.Block.Id, ct));
        Assert.Equal(0, await db.AgentRunSessionBundles.IgnoreQueryFilters().CountAsync(b => b.BlockId == tree.Block.Id, ct));
        Assert.Null(harness.Storage.GetTestFile(tree.ExecutedWorkflow.OrganizationId!, StorageCategories.AgentSessionBundles, tree.BundleKey!));
    }

    private static async Task AssertTreeIntactAsync(TreeFixture harness, SeededTree tree, CancellationToken ct)
    {
        UserDataDbContext db = harness.Context;
        Assert.Equal(1, await db.WorkflowExecutions.IgnoreQueryFilters().CountAsync(e => e.Id == tree.Execution.Id, ct));
        Assert.Equal(1, await db.Workflows.IgnoreQueryFilters().CountAsync(w => w.Id == tree.ExecutedWorkflow.Id, ct));
        Assert.Equal(1, await db.Blocks.IgnoreQueryFilters().CountAsync(b => b.Id == tree.Block.Id, ct));
        Assert.Equal(1, await db.BlockResponses.IgnoreQueryFilters().CountAsync(r => r.BlockId == tree.Block.Id, ct));
        Assert.Equal(1, await db.WorkflowExecutionLogs.IgnoreQueryFilters().CountAsync(l => l.WorkflowExecutionId == tree.Execution.Id, ct));
        Assert.Equal(1, await db.WorkflowReports.IgnoreQueryFilters().CountAsync(r => r.WorkflowExecutionId == tree.Execution.Id, ct));
        Assert.Equal(1, await db.ReportParagraphs.IgnoreQueryFilters().CountAsync(p => p.Id == tree.Paragraph.Id, ct));
        Assert.Equal(1, await db.DocumentInfos.IgnoreQueryFilters().CountAsync(di => di.JobId == tree.Execution.JobId && di.DocumentId == tree.OutputDocument.Id, ct));
        Assert.Equal(1, await db.Documents.IgnoreQueryFilters().CountAsync(d => d.Id == tree.OutputDocument.Id, ct));
        Assert.Equal(1, await db.Pages.IgnoreQueryFilters().CountAsync(p => p.DocumentId == tree.OutputDocument.Id, ct));
        Assert.Equal(1, await CountVectorsAsync(harness, [tree.PageEmbedding.Id], ct));
        Assert.Equal(1, await db.FileMetadata.IgnoreQueryFilters().CountAsync(fm => fm.Id == tree.OutputDocument.FileMetadataId, ct));
        Assert.Equal(1, await db.Files.IgnoreQueryFilters().CountAsync(f => f.Id == tree.OutputBlob.Id, ct));
    }

    private static Task<int> CountVectorsAsync(TreeFixture harness, string[] ids, CancellationToken ct)
    {
        return harness.Context.EmbeddingVectors.IgnoreQueryFilters().CountAsync(v => ids.Contains(v.Id), ct);
    }

    /// <summary>
    /// One execution as a real run leaves it: the executed clone with a section, a block and
    /// everything a block persists, the logs, the report tree, a counterparty link, an output
    /// document its job recorded, and — when asked — the agent-run evidence a block accrues.
    /// </summary>
    private static SeededTree SeedTree(
        TreeFixture harness,
        string organizationId,
        string jobId,
        bool withAgentEvidence,
        StorageProvider outputProvider = StorageProvider.Database)
    {
        UserDataDbContext db = harness.Context;

        var clone = new Workflow { Id = Guid.NewGuid().ToString(), Name = $"clone-{jobId}", OrganizationId = organizationId };
        var section = new Section("main", clone) { Id = Guid.NewGuid().ToString() };
        var block = new Block(BlockTypes.Agent, section);
        _ = new BlockResponse(block) { MarkdownResponse = "answer" };
        _ = new TextInput(block, "question", "what is the fee");
        _ = new RagSettings(block);
        _ = new DocumentClass(clone, OUTPUT_CLASS) { IsOutputDocument = true };
        _ = new DocumentClass(clone, INPUT_CLASS);
        _ = new WorkflowVariable(clone, "period");
        _ = new WorkflowRegressionField(clone, "fee");
        _ = new WorkflowSignatureField(clone, "analyst");
        _ = db.Workflows.Add(clone);

        var counterparty = new Counterparty(organizationId, $"cp-{jobId}");
        _ = db.Counterparties.Add(counterparty);

        var execution = new WorkflowExecution
        {
            Id = Guid.NewGuid().ToString(),
            OrganizationId = organizationId,
            WorkflowId = clone.Id,
            JobId = jobId,
            WorkflowState = WorkflowState.Draft,
        };
        _ = db.WorkflowExecutions.Add(execution);
        _ = db.WorkflowExecutionCounterparties.Add(new WorkflowExecutionCounterparty
        {
            WorkflowExecutionId = execution.Id,
            CounterpartyId = counterparty.Id,
        });

        var report = new WorkflowReport { OrganizationId = organizationId, WorkflowExecutionId = execution.Id, Title = "Report" };
        var reportSection = new ReportSection { Order = 0, Title = "Fees" };
        var paragraph = new ReportParagraph { Content = "Management fee: 1.5%", BlockId = block.Id };
        reportSection.Add(paragraph);
        report.Sections.Add(reportSection);
        _ = db.WorkflowReports.Add(report);

        var log = new WorkflowExecutionLog
        {
            WorkflowExecutionId = execution.Id,
            WorkflowId = clone.Id,
            BlockId = block.Id,
            Prompt = "the whole prompt",
        };
        _ = db.WorkflowExecutionLogs.Add(log);
        db.Entry(log).Property("ReportParagraphId").CurrentValue = paragraph.Id;

        Document outputDocument = SeedDocument(harness, organizationId, $"report-{jobId}.xlsx", outputProvider, out File outputBlob);
        _ = db.DocumentInfos.Add(new DocumentInfo(outputDocument.Id) { JobId = jobId, DocumentClass = OUTPUT_CLASS });
        Page page = outputDocument.Pages.Single();
        _ = db.BlockCitationTraces.Add(new BlockCitationTrace { OrganizationId = organizationId, BlockId = block.Id, Content = "{}" });

        string? bundleKey = null;
        if (withAgentEvidence)
        {
            string runExecutionId = AgentRunIds.For("exec", block.Id, AgentRunIds.WORKER_ROLE, AgentRunIds.FIRST_ROUND, AgentRunIds.FIRST_PASS);
            _ = db.AgentRunOutputs.Add(new AgentRunOutput
            {
                OrganizationId = organizationId,
                BlockId = block.Id,
                Round = AgentRunIds.FIRST_ROUND,
                RunExecutionId = runExecutionId,
            });
            _ = db.AgentRunTraceFiles.Add(new AgentRunTraceFile
            {
                OrganizationId = organizationId,
                BlockId = block.Id,
                RunExecutionId = runExecutionId,
                Attempt = AgentRunIds.FIRST_PASS,
                Path = "trace.jsonl",
                Sha256 = new string('a', 64),
                SizeBytes = _bytes.Length,
                Content = _bytes,
            });
            var bundle = new AgentRunSessionBundle
            {
                OrganizationId = organizationId,
                BlockId = block.Id,
                Round = AgentRunIds.FIRST_ROUND,
                RunExecutionId = runExecutionId,
                Attempt = AgentRunIds.FIRST_PASS,
                Segment = AgentRunCheckpoint.TERMINAL_SEGMENT,
                Sha256 = new string('b', 64),
                SizeBytes = _bytes.Length,
                StorageProvider = StorageProvider.Database.ToString(),
                StorageKey = AgentRunSessionBundleStorage.NewStorageKey(),
            };
            _ = db.AgentRunSessionBundles.Add(bundle);
            harness.Storage.AddTestFile(organizationId, StorageCategories.AgentSessionBundles, bundle.StorageKey, _bytes);
            bundleKey = bundle.StorageKey;
        }

        return new SeededTree(execution, clone, block, report, paragraph, outputDocument, page.EmbeddingVector!, outputBlob, counterparty, bundleKey);
    }

    private static Document SeedDocument(TreeFixture harness, string organizationId, string fileName, StorageProvider provider, out File blob)
    {
        UserDataDbContext db = harness.Context;
        blob = new File(_bytes);
        var metadata = new FileMetadata(organizationId, fileName, _bytes.Length, provider, blob.Id);
        var document = new Document(metadata, Utils.DetermineMediaTypeFromExtension(Path.GetExtension(fileName))) { Name = fileName };
        _ = new Page(document, 1, 1) { EmbeddingVector = new EmbeddingVector("model", [0.2]) };

        if (provider == StorageProvider.Database)
        {
            _ = db.Files.Add(blob);
        }
        else
        {
            harness.Storage.AddTestFile(organizationId, StorageCategories.Documents, blob.Id, _bytes);
        }

        _ = db.FileMetadata.Add(metadata);
        _ = db.Documents.Add(document);

        return document;
    }

    private sealed record SeededTree(
        WorkflowExecution Execution,
        Workflow ExecutedWorkflow,
        Block Block,
        WorkflowReport Report,
        ReportParagraph Paragraph,
        Document OutputDocument,
        EmbeddingVector PageEmbedding,
        File OutputBlob,
        Counterparty Counterparty,
        string? BundleKey);

    private sealed class TreeFixture : IDisposable
    {
        public SqliteUserDataDbContext Context => _harness.Context;
        public SoftDeleteCleanupService Service => _harness.Service;
        public TestFileStorageService Storage { get; } = new();

        private readonly SqliteCleanupServiceHarness _harness;

        public TreeFixture()
        {
            _harness = new SqliteCleanupServiceHarness(new FileStorageManager(
                new TestFileStorageResolver(Storage),
                Options.Create(new FileStorageSettings { DefaultProvider = "Database" })));
            Context.Organizations.AddRange(
                new Organization("Org A", ORG_A),
                new Organization("Org B", ORG_B));
            _ = Context.SaveChanges();
        }

        public void Dispose()
        {
            _harness.Dispose();
        }
    }
}
