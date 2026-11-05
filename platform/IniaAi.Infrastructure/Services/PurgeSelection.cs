using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Validation;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The rows one purge condemns, stated as queries so the ordered delete never learns how they
/// were chosen. The retention cleanup condemns whatever was soft-deleted past a cutoff; a
/// validation trim condemns named execution trees outright. Both walk the same statements in the
/// same order, so an inbound key cleared for one is cleared for the other.
/// </summary>
internal sealed class PurgeSelection
{
    public required IQueryable<Document> Documents { get; init; }

    public required IQueryable<Workflow> Workflows { get; init; }

    public required IQueryable<WorkflowExecution> Executions { get; init; }

    public required IQueryable<FileMetadata> FileMetadata { get; init; }

    public required IQueryable<File> Files { get; init; }

    public required IQueryable<EmbeddingVector> OrphanedEmbeddingVectors { get; init; }

    public required IQueryable<Counterparty> Counterparties { get; init; }

    public required IQueryable<CounterpartyType> CounterpartyTypes { get; init; }

    public required IQueryable<WorkflowReport> Reports { get; init; }

    public required IQueryable<ReportSection> ReportSections { get; init; }

    public required IQueryable<ReportParagraph> ReportParagraphs { get; init; }

    public required IQueryable<ReportDataOutput> ReportDataOutputs { get; init; }

    public required IQueryable<Block> BlocksOfWorkflows { get; init; }

    public required IQueryable<Block> SelfDeletedBlocksInOrgSections { get; init; }

    public required IQueryable<Block> OrphanedSelfDeletedBlocks { get; init; }

    public required IQueryable<DocumentInfo> DocumentInfos { get; init; }

    public required IQueryable<ValidationCase> ValidationCases { get; init; }

    public required IQueryable<ValidationRun> SelfDeletedValidationRuns { get; init; }

    public required IQueryable<ValidationSuite> ValidationSuites { get; init; }

    public IQueryable<string> DocumentIds => Documents.Select(d => d.Id);

    public IQueryable<string> WorkflowIds => Workflows.Select(w => w.Id);

    public IQueryable<string> ExecutionIds => Executions.Select(e => e.Id);

    public IQueryable<string> FileMetadataIds => FileMetadata.Select(fm => fm.Id);

    public IQueryable<string> CounterpartyIds => Counterparties.Select(c => c.Id);

    public IQueryable<string> ValidationCaseIds => ValidationCases.Select(c => c.Id);

    public IQueryable<string> BlockIdsOfWorkflows => BlocksOfWorkflows.Select(b => b.Id);

    /// <summary>
    /// Every Block row this purge removes — the union of the three condemnation branches, so the
    /// counted Block deletes and the bundle-byte release can never disagree.
    /// </summary>
    public IQueryable<string> CondemnedBlockIds => BlockIdsOfWorkflows
        .Union(SelfDeletedBlocksInOrgSections.Select(b => b.Id))
        .Union(OrphanedSelfDeletedBlocks.Select(b => b.Id));

    /// <summary>
    /// The retention cleanup's selection: every soft-deletable root of one organization whose
    /// deletion is older than the cutoff, plus the rows that hang beneath a condemned root. Rows no
    /// single organization owns (counterparty types, blocks with no section) go by
    /// <paramref name="sharedCutoff"/>, so one organization's short retention never shortens them.
    /// </summary>
    public static PurgeSelection SoftDeletedBefore(UserDataDbContext db, string organizationId, DateTime cutoff, DateTime sharedCutoff)
    {
        IQueryable<Document> documents = db.Documents.IgnoreQueryFilters()
            .Where(d => d.IsDeleted && d.OrganizationId == organizationId && d.DeletedAt <= cutoff);
        IQueryable<Workflow> workflows = db.Workflows.IgnoreQueryFilters()
            .Where(w => w.IsDeleted && w.OrganizationId == organizationId && w.DeletedAt <= cutoff);
        IQueryable<WorkflowExecution> executions = db.WorkflowExecutions.IgnoreQueryFilters()
            .Where(we => we.IsDeleted && we.OrganizationId == organizationId && we.DeletedAt <= cutoff);
        IQueryable<string> executionIds = executions.Select(we => we.Id);
        IQueryable<WorkflowReport> reports = SoftDeleteCleanupService.BuildDeletedReportsQuery(
            db.WorkflowReports, executionIds, organizationId, cutoff);
        IQueryable<string> reportIds = reports.Select(r => r.Id);
        IQueryable<ReportSection> sections = db.ReportSections.IgnoreQueryFilters()
            .Where(s => reportIds.Contains(s.WorkflowReportId)
                || (s.IsDeleted && s.DeletedAt <= cutoff
                    && s.WorkflowReport.OrganizationId == organizationId));
        IQueryable<string> sectionIds = sections.Select(s => s.Id);
        IQueryable<string> orgSectionIds = SectionIdsOf(db, SoftDeleteCleanupService.BuildOrgWorkflowIdsQuery(db.Workflows, organizationId));

        return new PurgeSelection
        {
            Documents = documents,
            Workflows = workflows,
            Executions = executions,
            FileMetadata = db.FileMetadata.IgnoreQueryFilters()
                .Where(fm => fm.IsDeleted && fm.OrganizationId == organizationId && fm.DeletedAt <= cutoff),
            Files = db.Files.IgnoreQueryFilters()
                .Where(f => f.IsDeleted && f.OrganizationId == organizationId && f.DeletedAt <= cutoff),
            OrphanedEmbeddingVectors = Unreferenced(db, db.EmbeddingVectors.IgnoreQueryFilters()),
            Counterparties = db.Counterparties.IgnoreQueryFilters()
                .Where(c => c.IsDeleted && c.OrganizationId == organizationId && c.DeletedAt <= cutoff),
            CounterpartyTypes = db.CounterpartyTypes.IgnoreQueryFilters()
                .Where(cType => cType.IsDeleted && cType.DeletedAt <= sharedCutoff
                    && !db.Counterparties.IgnoreQueryFilters()
                        .Where(c => !c.IsDeleted)
                        .SelectMany(c => c.CounterpartyTypes)
                        .Any(t => t.Id == cType.Id)),
            Reports = reports,
            ReportSections = sections,
            ReportParagraphs = db.ReportParagraphs.IgnoreQueryFilters()
                .Where(p => sectionIds.Contains(p.SectionId)
                    || (p.IsDeleted && p.DeletedAt <= cutoff
                        && p.Section.WorkflowReport.OrganizationId == organizationId)),
            ReportDataOutputs = db.ReportDataOutputs.IgnoreQueryFilters().Where(o => reportIds.Contains(o.WorkflowReportId)),
            BlocksOfWorkflows = BlocksOf(db, workflows),
            SelfDeletedBlocksInOrgSections = db.Blocks.IgnoreQueryFilters()
                .Where(b => b.IsDeleted && b.DeletedAt <= cutoff
                    && b.SectionId != null
                    && orgSectionIds.Contains(b.SectionId)),
            OrphanedSelfDeletedBlocks = db.Blocks.IgnoreQueryFilters()
                .Where(b => b.IsDeleted && b.DeletedAt <= sharedCutoff && b.SectionId == null),
            DocumentInfos = DocumentInfosOf(db, documents, executions),
            ValidationCases = db.ValidationCases.IgnoreQueryFilters()
                .Where(c => c.IsDeleted && c.OrganizationId == organizationId && c.DeletedAt <= cutoff),
            SelfDeletedValidationRuns = db.ValidationRuns.IgnoreQueryFilters()
                .Where(r => r.IsDeleted && r.OrganizationId == organizationId && r.DeletedAt <= cutoff),
            ValidationSuites = db.ValidationSuites.IgnoreQueryFilters()
                .Where(s => s.IsDeleted && s.OrganizationId == organizationId && s.DeletedAt <= cutoff),
        };
    }

    /// <summary>
    /// A trim's selection: the named executions of one organization, the clones they ran and the
    /// output documents their jobs produced, with the file metadata and stored bytes behind those
    /// documents and the vectors their pages leave behind. Nothing that was merely
    /// soft-deleted is touched, and no orphan the trim did not make — the retention cleanup owns
    /// both. The clone, document, metadata, blob and vector ids are read now rather than joined
    /// later: the ordered delete removes the executions before their clones, the documents before
    /// their metadata and the metadata before its blobs, so a join through the parent would find
    /// nothing by the time the child's turn came.
    /// </summary>
    public static async Task<PurgeSelection> ExecutionTreesAsync(
        UserDataDbContext db,
        string organizationId,
        IReadOnlyCollection<string> rootExecutionIds,
        CancellationToken ct)
    {
        IQueryable<WorkflowExecution> executions = db.WorkflowExecutions.IgnoreQueryFilters()
            .Where(we => we.OrganizationId == organizationId && rootExecutionIds.Contains(we.Id));
        IQueryable<string> executionIds = executions.Select(we => we.Id);
        List<string> workflowIds = await executions.Where(we => we.WorkflowId != null).Select(we => we.WorkflowId!).ToListAsync(ct);
        IQueryable<Workflow> workflows = db.Workflows.IgnoreQueryFilters()
            .Where(w => w.OrganizationId == organizationId && workflowIds.Contains(w.Id));
        List<string> documentIds = await OutputDocumentIdsAsync(db, executions, ct);
        IQueryable<Document> documents = db.Documents.IgnoreQueryFilters()
            .Where(d => d.OrganizationId == organizationId && documentIds.Contains(d.Id));
        List<string> fileMetadataIds = await documents.Select(d => d.FileMetadataId).ToListAsync(ct);
        IQueryable<FileMetadata> fileMetadata = db.FileMetadata.IgnoreQueryFilters()
            .Where(fm => fm.OrganizationId == organizationId && fileMetadataIds.Contains(fm.Id));
        List<string> databaseBlobIds = await fileMetadata
            .Where(fm => fm.StorageProvider == StorageProvider.Database && fm.ExternalStoragePath != null)
            .Select(fm => fm.ExternalStoragePath!)
            .ToListAsync(ct);
        List<string> embeddingVectorIds = await EmbeddingVectorIdsAsync(db, documentIds, ct);
        IQueryable<WorkflowReport> reports = db.WorkflowReports.IgnoreQueryFilters()
            .Where(r => r.WorkflowExecutionId != null && executionIds.Contains(r.WorkflowExecutionId));
        IQueryable<string> reportIds = reports.Select(r => r.Id);
        IQueryable<ReportSection> sections = db.ReportSections.IgnoreQueryFilters()
            .Where(s => reportIds.Contains(s.WorkflowReportId));
        IQueryable<string> sectionIds = sections.Select(s => s.Id);

        return new PurgeSelection
        {
            Documents = documents,
            Workflows = workflows,
            Executions = executions,
            FileMetadata = fileMetadata,
            Files = db.Files.IgnoreQueryFilters().Where(f => databaseBlobIds.Contains(f.Id)),
            OrphanedEmbeddingVectors = Unreferenced(db, db.EmbeddingVectors.IgnoreQueryFilters().Where(ev => embeddingVectorIds.Contains(ev.Id))),
            Counterparties = None(db.Counterparties),
            CounterpartyTypes = None(db.CounterpartyTypes),
            Reports = reports,
            ReportSections = sections,
            ReportParagraphs = db.ReportParagraphs.IgnoreQueryFilters().Where(p => sectionIds.Contains(p.SectionId)),
            ReportDataOutputs = db.ReportDataOutputs.IgnoreQueryFilters().Where(o => reportIds.Contains(o.WorkflowReportId)),
            BlocksOfWorkflows = BlocksOf(db, workflows),
            SelfDeletedBlocksInOrgSections = None(db.Blocks),
            OrphanedSelfDeletedBlocks = None(db.Blocks),
            DocumentInfos = DocumentInfosOf(db, documents, executions),
            ValidationCases = None(db.ValidationCases),
            SelfDeletedValidationRuns = None(db.ValidationRuns),
            ValidationSuites = None(db.ValidationSuites),
        };
    }

    /// <summary>
    /// The documents the executions produced and nothing else reads: those their jobs recorded
    /// under one of their clones' output classes, minus any that a job outside the tree also
    /// records. Input documents are shared by id and are never among these.
    /// </summary>
    private static Task<List<string>> OutputDocumentIdsAsync(
        UserDataDbContext db,
        IQueryable<WorkflowExecution> executions,
        CancellationToken ct)
    {
        IQueryable<string> jobIds = executions.Where(we => we.JobId != null).Select(we => we.JobId!);

        return db.DocumentInfos.IgnoreQueryFilters()
            .Where(di => di.JobId != null
                && executions.Any(we => we.JobId == di.JobId
                    && db.DocumentClasses.IgnoreQueryFilters()
                        .Any(dc => dc.WorkflowId == we.WorkflowId && dc.IsOutputDocument && dc.Key == di.DocumentClass))
                && !db.DocumentInfos.IgnoreQueryFilters()
                    .Any(other => other.DocumentId == di.DocumentId && (other.JobId == null || !jobIds.Contains(other.JobId))))
            .Select(di => di.DocumentId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>
    /// The vectors the pages of the given documents point at — the ones the trim's page delete
    /// orphans.
    /// </summary>
    private static async Task<List<string>> EmbeddingVectorIdsAsync(
        UserDataDbContext db,
        IReadOnlyCollection<string> documentIds,
        CancellationToken ct)
    {
        return await db.Pages.IgnoreQueryFilters()
            .Where(p => documentIds.Contains(p.DocumentId) && p.EmbeddingVectorId != null)
            .Select(p => p.EmbeddingVectorId!)
            .ToListAsync(ct);
    }

    /// <summary>
    /// The given vectors no page points at any more. Asked inside the transaction,
    /// after the page delete, so the answer is what the delete left behind.
    /// </summary>
    private static IQueryable<EmbeddingVector> Unreferenced(UserDataDbContext db, IQueryable<EmbeddingVector> vectors)
    {
        return vectors.Where(ev => !db.Pages.IgnoreQueryFilters().Any(p => p.EmbeddingVectorId == ev.Id));
    }

    private static IQueryable<Block> BlocksOf(UserDataDbContext db, IQueryable<Workflow> workflows)
    {
        IQueryable<string> sectionIds = SectionIdsOf(db, workflows.Select(w => w.Id));

        return db.Blocks.IgnoreQueryFilters()
            .Where(b => b.SectionId != null && sectionIds.Contains(b.SectionId));
    }

    private static IQueryable<string> SectionIdsOf(UserDataDbContext db, IQueryable<string> workflowIds)
    {
        return db.Set<Section>().IgnoreQueryFilters()
            .Where(s => workflowIds.Contains(s.WorkflowId))
            .Select(s => s.Id);
    }

    /// <summary>
    /// The document-info rows of condemned documents, and those of condemned executions' jobs:
    /// a job's record of the input it read is an orphan once the execution is gone, and the
    /// shared input document it points at stays exactly as it was.
    /// </summary>
    private static IQueryable<DocumentInfo> DocumentInfosOf(
        UserDataDbContext db,
        IQueryable<Document> documents,
        IQueryable<WorkflowExecution> executions)
    {
        IQueryable<string> documentIds = documents.Select(d => d.Id);
        IQueryable<string> jobIds = executions.Where(we => we.JobId != null).Select(we => we.JobId!);

        return db.DocumentInfos.IgnoreQueryFilters()
            .Where(di => documentIds.Contains(di.DocumentId) || (di.JobId != null && jobIds.Contains(di.JobId)));
    }

    private static IQueryable<T> None<T>(DbSet<T> set) where T : class
    {
        return set.IgnoreQueryFilters().Where(_ => false);
    }
}
