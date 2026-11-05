using System.Diagnostics;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.SelfServe;
using IniaAi.Domain.Entities.Testing;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.Validation;
using IniaAi.Domain.Entities.Verification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Graph;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Hard-deletes soft-deleted records older than a retention window, and removes named execution
/// trees outright for the validation retention trim, through one ordered delete.
/// Preserves TokenUsages by nullifying their foreign keys before deleting parent records.
/// Also removes orphaned files from external storage (MinIO/S3).
/// </summary>
public class SoftDeleteCleanupService : ISoftDeleteCleanupService
{
    private readonly UserDataDbContext _db;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly IReadOnlyList<IDocumentPurgeStep> _documentPurgeSteps;
    private readonly ILogger<SoftDeleteCleanupService> _logger;

    /// <summary>
    /// All FK relationships handled by this cleanup service.
    /// Used by schema drift detection tests to ensure completeness.
    /// If the test fails due to a new relationship, add it here and
    /// IMPLEMENT the necessary cleanup logic in PurgeAsync.
    /// </summary>
    public static readonly HashSet<string> HandledRelationships =
    [
        // Phase 0: TokenUsage FK nullification
        "TokenUsage.WorkflowExecutionId -> WorkflowExecution",
        "TokenUsage.DocumentId -> Document",

        // Layer 1: Deepest leaves
        "Page.DocumentId -> Document",
        "EmbeddingVector (orphan cleanup)",
        "DocumentInfo.DocumentId -> Document",
        "DocumentInfo.JobId -> WorkflowExecution (orphan cleanup, no FK)",
        "BlockResponse.BlockId -> Block (via Workflow)",
        "RagSettings.BlockId -> Block (via Workflow)",
        "TextInput.BlockId -> Block (via Workflow)",

        // Layer 2: Children of deleted WorkflowExecutions
        "WorkflowExecutionLog.WorkflowId -> Workflow",
        "WorkflowExecutionLog.WorkflowExecutionId -> WorkflowExecution",
        "WorkflowExecutionLog.BlockId -> Block",
        "WorkflowExecutionLog.ReportParagraphId -> ReportParagraph",
        "WorkflowExecutionCounterparty.WorkflowExecutionId -> WorkflowExecution",
        "WorkflowReport.WorkflowExecutionId -> WorkflowExecution",
        "WorkflowReport.CounterpartyId -> Counterparty",
        "ReportParagraph.BlockId -> Block",
        "ReportDataOutput.BlockId -> Block",

        // Layer 3: Children of deleted Counterparties
        "CounterpartyCounterpartyType.CounterpartiesId -> Counterparty (raw SQL)",
        "WorkflowExecution.CounterpartyId -> Counterparty (nullify)",

        // Layer 4: Self-deleted entities
        "Block.SectionId -> Section",
        "Block (self-deleted)",
        "Section (self-deleted)",
        "DocumentClass.WorkflowId -> Workflow",
        "WorkflowVariable.WorkflowId -> Workflow",
        "WorkflowRegressionField.WorkflowId -> Workflow",
        "WorkflowExecution (self-deleted)",
        "Workflow (self-deleted)",
        "Document (self-deleted)",
        "FileMetadata (self-deleted)",
        "File (self-deleted)",
        "CounterpartyType (self-deleted)",
        "Counterparty (self-deleted)",
        "Json2XlsxTemplate.FileMetadataId -> FileMetadata",
        "Json2XlsxTemplateVersion.FileMetadataId -> FileMetadata",

        // Validation entities: runs of a purged case first (the model's one real FK, DB Cascade),
        // then the cases, then suites. Reference audit logs are append-only by design — no sweep.
        "ValidationRun.CaseId -> ValidationCase",
        "ValidationRun (self-deleted)",
        "ValidationCase (self-deleted)",
        "ValidationSuite (self-deleted)",

        // File storage cleanup
        "FileMetadata -> external storage (file cleanup)",
        "AgentRunSessionBundle.BlockId -> Block (DB cascade; bytes via file cleanup)",
    ];

    /// <summary>
    /// The foreign-key-free rows that carry the id of something this service purges, each with what
    /// the sweep does to it. A feature that owns such a row declares it on its
    /// <see cref="IDocumentPurgeStep"/> instead. The schema drift test derives the same list from the
    /// model and fails when the two disagree.
    /// </summary>
    public static readonly IReadOnlyList<LooseReferenceDisposition> LooseReferenceDispositions =
    [
        new(typeof(DocumentUnlockAuditLog), [nameof(DocumentUnlockAuditLog.DocumentId)], []),
        new(typeof(Domain.Entities.Documents.File), [nameof(Domain.Entities.Documents.File.DocumentId)], [], RowDeleted: true),
        new(typeof(GraphNode), [nameof(GraphNode.RunId)], [], RowDeleted: true),
        new(typeof(GraphEdge), [nameof(GraphEdge.RunId)], [], RowDeleted: true),
        new(typeof(HelpRequest), [nameof(HelpRequest.WorkflowExecutionId)], []),
        new(typeof(ReportFeedbackItem), [nameof(ReportFeedbackItem.WorkflowExecutionId)], [], RowDeleted: true),
        new(typeof(SupportGrant), [nameof(SupportGrant.WorkflowExecutionId)], []),
        new(typeof(TokenUsageExecutionTotal), [nameof(TokenUsageExecutionTotal.WorkflowExecutionId)], []),
        new(
            typeof(ValidationCase),
            [nameof(ValidationCase.BaselineDocumentId), nameof(ValidationCase.BaselineExecutionId), nameof(ValidationCase.SourceCaseId)],
            []),
        new(typeof(ValidationReferenceAuditLog), [nameof(ValidationReferenceAuditLog.CaseId)], []),
        new(typeof(ValidationSuite), [nameof(ValidationSuite.SourceSuiteId)], []),
        new(
            typeof(VerificationCriterionRecord),
            [
                nameof(VerificationCriterionRecord.WorkflowExecutionId),
                nameof(VerificationCriterionRecord.RunExecutionId),
                nameof(VerificationCriterionRecord.VerifierBlockId),
                nameof(VerificationCriterionRecord.MemberBlockId),
            ],
            [nameof(VerificationCriterionRecord.Feedback), nameof(VerificationCriterionRecord.EvidenceSpans)]),
        new(
            typeof(WorkflowBaselineRegistration),
            [nameof(WorkflowBaselineRegistration.CounterpartyId), nameof(WorkflowBaselineRegistration.ExecutionId), nameof(WorkflowBaselineRegistration.SourceExecutionId)],
            []),
        new(typeof(WorkflowRegressionRun), [nameof(WorkflowRegressionRun.BaselineExecutionId), nameof(WorkflowRegressionRun.RerunExecutionId)], []),
    ];

    public SoftDeleteCleanupService(
        UserDataDbContext db,
        IFileStorageManager fileStorageManager,
        IEnumerable<IDocumentPurgeStep> documentPurgeSteps,
        ILogger<SoftDeleteCleanupService> logger)
    {
        _db = db;
        _fileStorageManager = fileStorageManager;
        _documentPurgeSteps = [.. documentPurgeSteps];
        _logger = logger;
    }

    /// <summary>
    /// Returns the IDs of every Workflow row owned by <paramref name="organizationId"/>.
    /// Sections live in their own table now, so there are no parent-rooted child
    /// Workflows to expand into.
    /// </summary>
    internal static IQueryable<string> BuildOrgWorkflowIdsQuery(
        DbSet<Workflow> workflows,
        string organizationId)
    {
        return workflows.IgnoreQueryFilters()
            .Where(w => w.OrganizationId == organizationId)
            .Select(w => w.Id);
    }

    /// <summary>
    /// Returns the IDs of every Workflow row that's been soft-deleted past
    /// <paramref name="cutoff"/>. Sections cascade via their own FK; no parent
    /// expansion needed.
    /// </summary>
    internal static IQueryable<string> BuildDeletedWorkflowIdsQuery(
        DbSet<Workflow> workflows,
        string organizationId,
        DateTime cutoff)
    {
        return workflows.IgnoreQueryFilters()
            .Where(w => w.IsDeleted
                && w.OrganizationId == organizationId
                && w.DeletedAt <= cutoff)
            .Select(w => w.Id);
    }

    /// <summary>
    /// Returns every WorkflowReport row to purge: those produced by an execution that's being
    /// deleted, plus reports soft-deleted directly past <paramref name="cutoff"/>. ReportSections
    /// and ReportParagraphs cascade via their own FKs, so only the report roots are scoped here.
    /// </summary>
    internal static IQueryable<WorkflowReport> BuildDeletedReportsQuery(
        DbSet<WorkflowReport> reports,
        IQueryable<string> deletedExecutionIds,
        string organizationId,
        DateTime cutoff)
    {
        return reports.IgnoreQueryFilters()
            .Where(r => (r.WorkflowExecutionId != null && deletedExecutionIds.Contains(r.WorkflowExecutionId))
                || (r.IsDeleted && r.OrganizationId == organizationId && r.DeletedAt <= cutoff));
    }

    /// <summary>
    /// The IDs of <see cref="BuildDeletedReportsQuery"/>.
    /// </summary>
    internal static IQueryable<string> BuildDeletedReportIdsQuery(
        DbSet<WorkflowReport> reports,
        IQueryable<string> deletedExecutionIds,
        string organizationId,
        DateTime cutoff)
    {
        return BuildDeletedReportsQuery(reports, deletedExecutionIds, organizationId, cutoff).Select(r => r.Id);
    }

    public async Task<CleanupResult> CleanupAsync(string organizationId, int retentionDays, CancellationToken ct = default, int? sharedRetentionDays = null)
    {
        DateTime now = DateTime.UtcNow;
        DateTime cutoff = now.AddDays(-retentionDays);
        DateTime sharedCutoff = now.AddDays(-(sharedRetentionDays ?? retentionDays));

        _logger.LogInformation(
            "SoftDeleteCleanup: Starting cleanup for org {OrgId}, retention {RetentionDays} days (cutoff: {Cutoff})",
            organizationId, retentionDays, cutoff);

        try
        {
            return await PurgeAsync(organizationId, PurgeSelection.SoftDeletedBefore(_db, organizationId, cutoff, sharedCutoff), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SoftDeleteCleanup: Org {OrgId} — Cleanup transaction failed", organizationId);
            return new CleanupResult(0, [], 0, [$"Transaction failed: {ex.Message}"]);
        }
    }

    public async Task<CleanupResult> PurgeExecutionTreesAsync(string organizationId, IReadOnlyCollection<string> executionIds, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SoftDeleteCleanup: Org {OrgId} — purging {Executions} execution tree(s)",
            organizationId, executionIds.Count);

        PurgeSelection selection = await PurgeSelection.ExecutionTreesAsync(_db, organizationId, executionIds, ct);

        return await PurgeAsync(organizationId, selection, ct);
    }

    /// <summary>
    /// The one ordered delete. Rows go leaves-first inside one transaction under the retrying
    /// execution strategy; a failed transaction propagates, so the caller knows nothing was
    /// removed. Stored bytes are released only after the commit, and a byte that could not be
    /// released is reported in the result rather than thrown.
    /// </summary>
    private async Task<CleanupResult> PurgeAsync(string organizationId, PurgeSelection selection, CancellationToken ct)
    {
        var deletedPerTable = new Dictionary<string, int>();
        var errors = new List<string>();
        int filesDeleted = 0;
        var stopwatch = Stopwatch.StartNew();

        // ═══════════════════════════════════════════════════════════════
        // Collect file storage info BEFORE the transaction (read-only query)
        // Files are deleted AFTER the transaction commits successfully
        // ═══════════════════════════════════════════════════════════════

        List<FileMetadata> filesToDelete = await selection.FileMetadata
            .Where(fm => fm.ExternalStoragePath != null
                && fm.StorageProvider != Domain.Enums.StorageProvider.Database
                && fm.StorageProvider != Domain.Enums.StorageProvider.NotDefined)
            .Select(fm => new FileMetadata
            {
                Id = fm.Id,
                OrganizationId = fm.OrganizationId,
                StorageProvider = fm.StorageProvider,
                ExternalStoragePath = fm.ExternalStoragePath,
            })
            .ToListAsync(ct);

        // Session bundles of blocks this run purges: the pointer rows go with their Block by DB
        // cascade inside the transaction, so the storage locations must be read out first or the
        // bytes become unfindable the moment the rows are gone. Released for EVERY provider,
        // Database included — unlike the FileMetadata sweep above, which skips Database because
        // those blobs are File rows deleted in-transaction; a bundle's blob under the Database
        // provider is an unreferenced Files row no other sweep can reach.
        IQueryable<string> preTransactionCondemnedBlockIds = selection.CondemnedBlockIds;
        List<StoredBytes> sessionBundlesToRelease = await _db.AgentRunSessionBundles
            .IgnoreQueryFilters()
            .Where(row => row.BlockId != null && preTransactionCondemnedBlockIds.Contains(row.BlockId))
            .Select(row => new StoredBytes(
                row.OrganizationId, row.RunExecutionId, row.StorageProvider, row.StorageKey))
            .ToListAsync(ct);

        IExecutionStrategy strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(_ => RunPurgeTransactionAsync(), ct);

        // ═══════════════════════════════════════════════════════════════
        // PHASE 2: File storage cleanup (AFTER transaction commit)
        // This ensures DB records are gone before we delete external files.
        // If file deletion fails, the DB is already clean — orphaned files
        // are acceptable (they'll be retried or cleaned up manually).
        // ═══════════════════════════════════════════════════════════════

        foreach (FileMetadata fm in filesToDelete)
        {
            try
            {
                await _fileStorageManager.DeleteFileAsync(
                    fm.StorageProvider.ToString(),
                    fm.OrganizationId,
                    StorageCategories.Documents,
                    fm.ExternalStoragePath!);
                filesDeleted++;
            }
            catch (Exception ex)
            {
                string error = $"Failed to delete file {fm.Id} from {fm.StorageProvider}: {ex.Message}";
                _logger.LogWarning(ex, "SoftDeleteCleanup: Org {OrgId} — {Error}", organizationId, error);
                errors.Add(error);
            }
        }

        if (filesToDelete.Count > 0)
        {
            _logger.LogInformation(
                "SoftDeleteCleanup: Org {OrgId} — Deleted {Count}/{Total} files from external storage",
                organizationId, filesDeleted, filesToDelete.Count);
        }

        foreach (StoredBytes bundle in sessionBundlesToRelease)
        {
            if (await AgentRunSessionBundleStorage.DeleteBytesQuietlyAsync(_fileStorageManager, bundle, _logger)
                is { } stranding)
            {
                errors.Add(
                    $"Failed to delete the session bundle bytes of run {bundle.RunExecutionId} "
                        + $"from {bundle.StorageProvider}: {stranding.Message}");
            }
            else
            {
                filesDeleted++;
            }
        }

        if (sessionBundlesToRelease.Count > 0)
        {
            _logger.LogInformation(
                "SoftDeleteCleanup: Org {OrgId} — Released the bytes of {Count} session bundle(s) of purged blocks",
                organizationId, sessionBundlesToRelease.Count);
        }

        stopwatch.Stop();
        int totalDeleted = deletedPerTable.Values.Sum();

        _logger.LogInformation(
            "SoftDeleteCleanup: Org {OrgId} — Complete: {TotalDeleted} records from {TableCount} tables, {FilesDeleted} files in {Duration}ms",
            organizationId, totalDeleted, deletedPerTable.Count(kv => kv.Value > 0), filesDeleted, stopwatch.ElapsedMilliseconds);

        return new CleanupResult(totalDeleted, deletedPerTable, filesDeleted, errors);

        async Task RunPurgeTransactionAsync()
        {
            deletedPerTable.Clear();

            await using IDbContextTransaction transaction =
                await _db.Database.BeginTransactionAsync(ct);

            // ═══════════════════════════════════════════════════════════════
            // PHASE 0: Preserve TokenUsages — nullify FKs before deleting parents
            // ═══════════════════════════════════════════════════════════════

            IQueryable<string> deletedExecutionIds = selection.ExecutionIds;
            IQueryable<string> deletedDocumentIds = selection.DocumentIds;
            IQueryable<string> deletedWorkflowIds = selection.WorkflowIds;
            IQueryable<string> blockIdsOfDeletedWorkflows = selection.BlockIdsOfWorkflows;

            int count = await _db.TokenUsages
                .IgnoreQueryFilters()
                .Where(t => t.WorkflowExecutionId != null && deletedExecutionIds.Contains(t.WorkflowExecutionId!))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowExecutionId, (string?)null), ct);
            LogStep(deletedPerTable, "TokenUsage.WorkflowExecutionId (nullified)", count, organizationId);

            count = await _db.TokenUsages
                .IgnoreQueryFilters()
                .Where(t => t.DocumentId != null && deletedDocumentIds.Contains(t.DocumentId!))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DocumentId, (string?)null), ct);
            LogStep(deletedPerTable, "TokenUsage.DocumentId (nullified)", count, organizationId);

            // ═══════════════════════════════════════════════════════════════
            // PHASE 1: Database cleanup — leaves to parents
            // ═══════════════════════════════════════════════════════════════

            // --- LAYER 1: Deepest leaves ---

            count = await _db.Pages
                .IgnoreQueryFilters()
                .Where(p => deletedDocumentIds.Contains(p.DocumentId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Pages", count, organizationId);

            count = await selection.OrphanedEmbeddingVectors.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "EmbeddingVectors (orphaned)", count, organizationId);

            count = await selection.DocumentInfos.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "DocumentInfos", count, organizationId);

            count = await _db.BlockResponses
                .IgnoreQueryFilters()
                .Where(br => br.BlockId != null && blockIdsOfDeletedWorkflows.Contains(br.BlockId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "BlockResponses", count, organizationId);

            count = await _db.RagSettings
                .IgnoreQueryFilters()
                .Where(rs => blockIdsOfDeletedWorkflows.Contains(rs.BlockId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "RagSettings", count, organizationId);

            count = await _db.TextInputs
                .IgnoreQueryFilters()
                .Where(ti => blockIdsOfDeletedWorkflows.Contains(ti.BlockId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "TextInputs", count, organizationId);

            // --- LAYER 2: Children of deleted WorkflowExecutions ---

            // Rows with no FK that outlive their parent: null the customer text, keep the row.

            // Failure-record rows outlive their execution by design (no FK): text follows the
            // customer, aggregates are permanent — null the two text columns, keep the rest.
            count = await _db.VerificationCriterionRecords
                .IgnoreQueryFilters()
                .Where(r => (r.Feedback != null || r.EvidenceSpans != null)
                    && deletedExecutionIds.Contains(r.WorkflowExecutionId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Feedback, (string?)null)
                    .SetProperty(r => r.EvidenceSpans, (string?)null), ct);
            LogStep(deletedPerTable, "VerificationCriterionRecord text (nullified)", count, organizationId);

            foreach (IDocumentPurgeStep step in _documentPurgeSteps)
            {
                count = await step.RunAsync(deletedDocumentIds, ct);
                LogStep(deletedPerTable, step.Name, count, organizationId);
            }

            // WorkflowExecutionLogs referencing deleted Workflows
            count = await _db.WorkflowExecutionLogs
                .IgnoreQueryFilters()
                .Where(log => log.WorkflowId != null && deletedWorkflowIds.Contains(log.WorkflowId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "WorkflowExecutionLogs (by Workflow)", count, organizationId);

            // WorkflowExecutionLogs referencing deleted Executions
            count = await _db.WorkflowExecutionLogs
                .IgnoreQueryFilters()
                .Where(log => deletedExecutionIds.Contains(log.WorkflowExecutionId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "WorkflowExecutionLogs (by Execution)", count, organizationId);

            // WorkflowExecutionLogs referencing Blocks of deleted Workflows
            count = await _db.WorkflowExecutionLogs
                .IgnoreQueryFilters()
                .Where(log => log.BlockId != null && blockIdsOfDeletedWorkflows.Contains(log.BlockId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "WorkflowExecutionLogs (by Block)", count, organizationId);

            // WorkflowExecutionCounterparties referencing deleted Executions
            count = await _db.WorkflowExecutionCounterparties
                .IgnoreQueryFilters()
                .Where(wec => deletedExecutionIds.Contains(wec.WorkflowExecutionId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "WorkflowExecutionCounterparties", count, organizationId);

            count = await _db.GraphEdges
                .Where(edge => deletedExecutionIds.Contains(edge.RunId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "GraphEdges", count, organizationId);

            count = await _db.GraphNodes
                .Where(node => deletedExecutionIds.Contains(node.RunId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "GraphNodes", count, organizationId);

            // Feedback quotes the report's text, so it goes with the run.
            count = await _db.ReportFeedbackItems
                .IgnoreQueryFilters()
                .Where(item => deletedExecutionIds.Contains(item.WorkflowExecutionId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "ReportFeedbackItems", count, organizationId);

            // Report tree (WorkflowReport -> ReportSection -> ReportParagraph).
            // Purge reports of deleted executions plus reports/sections/paragraphs that
            // were soft-deleted directly. Deleted leaves-first so the NoAction inbound
            // FKs (WorkflowExecutionLog.ReportParagraphId below) never block a delete.
            IQueryable<string> deletedReportParagraphIds = selection.ReportParagraphs.Select(p => p.Id);

            // Nullify WorkflowExecutionLog shadow FK to ReportParagraphs that will be deleted
            // (shadow property — no CLR property, EF.Property required)
            count = await _db.WorkflowExecutionLogs
                .IgnoreQueryFilters()
                .Where(log => deletedReportParagraphIds.Any(id => id == EF.Property<string?>(log, "ReportParagraphId")))
                .ExecuteUpdateAsync(s => s.SetProperty(
                    log => EF.Property<string?>(log, "ReportParagraphId"), (string?)null), ct);
            LogStep(deletedPerTable, "WorkflowExecutionLog.ReportParagraphId (nullified)", count, organizationId);

            // ReportParagraphs (leaves)
            count = await selection.ReportParagraphs.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "ReportParagraphs", count, organizationId);

            // ReportSections
            count = await selection.ReportSections.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "ReportSections", count, organizationId);

            count = await selection.ReportDataOutputs.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "ReportDataOutputs", count, organizationId);

            // WorkflowReports (roots)
            count = await selection.Reports.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "WorkflowReports", count, organizationId);

            // --- LAYER 3: Children of deleted Counterparties ---

            IQueryable<string> deletedCounterpartyIds = selection.CounterpartyIds;

            // CounterpartyCounterpartyType — no CLR entity, must use raw SQL
            // Use provider-specific quoting for schema-qualified table/column names
            string deleteSql = GetDeleteFromJoinTableSql("CounterpartyCounterpartyType", "CounterpartiesId");
            string[] counterpartyIdsToDelete = await deletedCounterpartyIds.ToArrayAsync(ct);
            count = 0;
            foreach (string cpId in counterpartyIdsToDelete)
            {
                count += await _db.Database.ExecuteSqlRawAsync(
                    deleteSql, [cpId], ct);
            }

            LogStep(deletedPerTable, "CounterpartyCounterpartyType", count, organizationId);

            // Nullify WorkflowExecution.CounterpartyId pointing to deleted Counterparties
            count = await _db.WorkflowExecutions
                .IgnoreQueryFilters()
                .Where(we => we.CounterpartyId != null && deletedCounterpartyIds.Contains(we.CounterpartyId))
                .ExecuteUpdateAsync(s => s.SetProperty(we => we.CounterpartyId, (string?)null), ct);
            LogStep(deletedPerTable, "WorkflowExecution.CounterpartyId (nullified)", count, organizationId);

            // Nullify WorkflowReport.CounterpartyId pointing to deleted Counterparties (surviving reports)
            count = await _db.WorkflowReports
                .IgnoreQueryFilters()
                .Where(r => r.CounterpartyId != null && deletedCounterpartyIds.Contains(r.CounterpartyId))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CounterpartyId, (string?)null), ct);
            LogStep(deletedPerTable, "WorkflowReport.CounterpartyId (nullified)", count, organizationId);

            // --- LAYER 4: The soft-deleted parents themselves ---

            // Nullify ReportParagraph.BlockId for surviving paragraphs that reference Blocks
            // about to be deleted (BlockId is a NoAction FK with no legacy equivalent — paragraphs
            // of purged executions are already gone, this covers paragraphs whose execution survives).
            IQueryable<string> condemnedBlockIds = selection.CondemnedBlockIds;
            count = await _db.ReportParagraphs
                .IgnoreQueryFilters()
                .Where(p => p.BlockId != null && condemnedBlockIds.Contains(p.BlockId))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.BlockId, (string?)null), ct);
            LogStep(deletedPerTable, "ReportParagraph.BlockId (nullified)", count, organizationId);

            // The same for data outputs; those of purged reports are already gone.
            count = await _db.ReportDataOutputs
                .IgnoreQueryFilters()
                .Where(output => output.BlockId != null && condemnedBlockIds.Contains(output.BlockId))
                .ExecuteUpdateAsync(s => s.SetProperty(output => output.BlockId, (string?)null), ct);
            LogStep(deletedPerTable, "ReportDataOutput.BlockId (nullified)", count, organizationId);

            // Blocks on deleted Workflows (scoped via Section)
            count = await selection.BlocksOfWorkflows.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Blocks (by Workflow)", count, organizationId);

            count = await selection.SelfDeletedBlocksInOrgSections.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Blocks (self-deleted)", count, organizationId);

            // Orphaned self-deleted Blocks (no SectionId — can't scope to org, safe to delete)
            count = await selection.OrphanedSelfDeletedBlocks.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Blocks (orphaned)", count, organizationId);

            // DocumentClasses on deleted Workflows
            count = await _db.Set<DocumentClass>()
                .IgnoreQueryFilters()
                .Where(dc => deletedWorkflowIds.Contains(dc.WorkflowId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "DocumentClasses", count, organizationId);

            // WorkflowVariables on deleted Workflows
            count = await _db.Set<WorkflowVariable>()
                .IgnoreQueryFilters()
                .Where(wv => deletedWorkflowIds.Contains(wv.WorkflowId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "WorkflowVariables", count, organizationId);

            // WorkflowRegressionFields on deleted Workflows
            count = await _db.Set<WorkflowRegressionField>()
                .IgnoreQueryFilters()
                .Where(rf => deletedWorkflowIds.Contains(rf.WorkflowId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "WorkflowRegressionFields", count, organizationId);

            // WorkflowSignatureFields on deleted Workflows
            count = await _db.Set<WorkflowSignatureField>()
                .IgnoreQueryFilters()
                .Where(sf => deletedWorkflowIds.Contains(sf.WorkflowId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "WorkflowSignatureFields", count, organizationId);

            // WorkflowExecutions
            count = await selection.Executions.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "WorkflowExecutions", count, organizationId);

            count = await selection.Workflows.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Workflows", count, organizationId);

            // Documents
            count = await selection.Documents.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Documents", count, organizationId);

            // Json2XlsxTemplates referencing FileMetadata that will be deleted
            IQueryable<string> deletedFileMetadataIds = selection.FileMetadataIds;

            count = await _db.Json2XlsxTemplates
                .IgnoreQueryFilters()
                .Where(t => deletedFileMetadataIds.Contains(t.FileMetadataId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Json2XlsxTemplates", count, organizationId);

            // Archived template versions share the same NoAction FK to FileMetadata; remove them
            // before the FileMetadata delete or the bulk delete violates the constraint.
            count = await _db.Json2XlsxTemplateVersions
                .IgnoreQueryFilters()
                .Where(v => deletedFileMetadataIds.Contains(v.FileMetadataId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Json2XlsxTemplateVersions", count, organizationId);

            // FileMetadata
            count = await selection.FileMetadata.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "FileMetadata", count, organizationId);

            // Files (database-stored blobs)
            count = await selection.Files.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Files", count, organizationId);

            // CounterpartyTypes — shared across orgs (no OrganizationId), but still
            // org-scoped by only deleting types that have no remaining non-deleted counterparties
            count = await selection.CounterpartyTypes.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "CounterpartyTypes", count, organizationId);

            // Counterparties
            count = await selection.Counterparties.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "Counterparties", count, organizationId);

            // Validation entities: runs before cases (the model's one real FK)

            IQueryable<string> deletedValidationCaseIds = selection.ValidationCaseIds;

            count = await _db.ValidationRuns
                .IgnoreQueryFilters()
                .Where(r => r.OrganizationId == organizationId && deletedValidationCaseIds.Contains(r.CaseId))
                .ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "ValidationRuns (by Case)", count, organizationId);

            count = await selection.SelfDeletedValidationRuns.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "ValidationRuns (self-deleted)", count, organizationId);

            count = await selection.ValidationCases.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "ValidationCases", count, organizationId);

            count = await selection.ValidationSuites.ExecuteDeleteAsync(ct);
            LogStep(deletedPerTable, "ValidationSuites", count, organizationId);

            await transaction.CommitAsync(ct);
        }
    }

    /// <summary>
    /// Returns a provider-agnostic DELETE SQL statement for the CounterpartyCounterpartyType join table.
    /// Uses the correct quoting convention for the current database provider.
    /// </summary>
    private string GetDeleteFromJoinTableSql(string tableName, string columnName)
    {
        string providerName = _db.Database.ProviderName?.ToLower() ?? string.Empty;

        if (providerName.Contains("npgsql"))
        {
            return $"""DELETE FROM "{UserDataDbContext.SCHEMA_NAME}"."{tableName}" WHERE "{columnName}" = @p0""";
        }

        if (providerName.Contains("mysql"))
        {
            return $"DELETE FROM `{UserDataDbContext.SCHEMA_NAME}`.`{tableName}` WHERE `{columnName}` = @p0";
        }

        // SQL Server (default)
        return $"DELETE FROM [{UserDataDbContext.SCHEMA_NAME}].[{tableName}] WHERE [{columnName}] = @p0";
    }

    private void LogStep(Dictionary<string, int> deletedPerTable, string tableName, int count, string organizationId)
    {
        deletedPerTable[tableName] = count;
        if (count > 0)
        {
            _logger.LogInformation(
                "SoftDeleteCleanup: Org {OrgId} — Deleted {Count} {TableName}",
                organizationId, count, tableName);
        }
    }
}
