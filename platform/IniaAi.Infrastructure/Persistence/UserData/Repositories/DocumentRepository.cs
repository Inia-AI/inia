using IniaAi.Common.Enums;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Specifications.Common;
using IniaAi.Infrastructure.Persistence.Abstractions;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Infrastructure.Persistence.UserData.Repositories;

public class DocumentRepository : RepositoryBase<Document>, IDocumentRepository
{
    public DocumentRepository(UserDataDbContext context)
        : base(context) { }

    public override async Task<Document?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        return await Entities
            .Include(d => d.FileMetadata)
            .Include(d => d.Pages)
                .ThenInclude(p => p.EmbeddingVector)
            .FirstOrDefaultAsync(d => d.Id == id.ToString() && !d.IsDeleted, cancellationToken: cancellationToken);
    }

    public async Task<Document?> GetDocumentByFileNameAndSize(string fileName, int sizeBytes, string organizationId)
    {
        return await Entities
            .Include(d => d.Pages)
                .ThenInclude(p => p.EmbeddingVector)
            .Include(d => d.FileMetadata)
            .FirstOrDefaultAsync(d => !d.IsDeleted && d.OrganizationId == organizationId && d.FileMetadata != null && d.FileMetadata.Name == fileName && d.FileMetadata.SizeBytes == sizeBytes);
    }

    public async Task<IEnumerable<Document>> FindByCounterpartyIdAsync(string counterpartyId)
    {
        return await Entities
            .Where(d => d.Counterparties.Any(c => c.Id == counterpartyId) && !d.IsDeleted)
            .ToListAsync();
    }

    public async Task<IEnumerable<DocumentInfo>> GetDocumentInfosByJobIdOrWorkTypeIdAsync(
        string jobId,
        string organizationId,
        string? workflowTypeId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job ID cannot be null or empty.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(organizationId))
        {
            throw new ArgumentException("Organization ID cannot be null or empty.", nameof(organizationId));
        }

        if (!string.IsNullOrWhiteSpace(workflowTypeId))
        {
            List<Document> documents = await Entities
                .Where(d => d.WorkflowTypeId == workflowTypeId && d.OrganizationId == organizationId)
                .ToListAsync();

            foreach (Document document in documents)
            {
                if (Context.Set<DocumentInfo>().Any(di => di.DocumentId == document.Id && di.JobId == jobId))
                {
                    continue;
                }

                var documentInfo = new DocumentInfo(document.Id)
                {
                    Document = document,
                    JobId = jobId,
                    DocumentClass = document.Class ?? string.Empty,
                };

                await AddDocumentInfoAsync(documentInfo);
            }

            _ = await Context.SaveChangesAsync();
        }

        IQueryable<DocumentInfo> query = Context.Set<DocumentInfo>()
            .AsSplitQuery()
            .Include(di => di.Document)
                .ThenInclude(d => d!.FileMetadata)
            .Include(di => di.Document)
                .ThenInclude(d => d!.Pages)
                    .ThenInclude(p => p.EmbeddingVector)
            .Where(di => di.JobId == jobId && di.Document!.OrganizationId == organizationId);

        List<DocumentInfo> documentInfos = await query.ToListAsync();

        foreach (DocumentInfo? documentInfo in documentInfos)
        {
            if (documentInfo.Document is null)
            {
                continue;
            }

            documentInfo.Document.CurrentDocumentInfo = documentInfo;
        }

        return documentInfos;
    }

    public async Task AddDocumentInfoAsync(DocumentInfo documentInfo)
    {
        _ = await Context.Set<DocumentInfo>().AddAsync(documentInfo);
    }

    public void RemoveDocumentInfo(DocumentInfo documentInfo)
    {
        _ = Context.Set<DocumentInfo>().Remove(documentInfo);
    }

    public async Task<(Document Document, bool Created)> GetOrCreateDocumentAsync(
        byte[] bytes,
        string organizationId,
        string? userId,
        string fileName,
        long fileSize,
        StorageProvider storageProvider,
        MediaType mediaType)
    {
        Document? existing = await FindExistingDocumentAsync(fileName, fileSize, organizationId);

        if (existing is not null)
        {
            return (existing, false);
        }

        Document created = await UploadNewFileCreateNewDocumentAsync(
            bytes,
            organizationId,
            userId,
            fileName,
            storageProvider,
            mediaType);

        return (created, true);
    }

    public async Task<DocumentInfo> CreateDocumentInfoAsync(Document document)
    {
        var documentInfo = new DocumentInfo(document.Id)
        {
            Document = document,
        };

        await AddDocumentInfoAsync(documentInfo);

        return documentInfo;
    }

    public async Task<Document?> FindExistingDocumentAsync(
        string fileName,
        long fileSize,
        string organizationId)
    {
        FileMetadata? fileMetadata = await GetFileMetadataByNameAndSizeAsync(
            Path.GetFileNameWithoutExtension(ControlCharacters.ToSpaces(fileName)),
            fileSize,
            organizationId);

        return fileMetadata is not null && fileMetadata.DocumentId is not null
            ? await GetByIdAsync(fileMetadata.DocumentId)
            : null;
    }

    public async Task<Document> UploadNewFileCreateNewDocumentAsync(
        byte[] bytes,
        string organizationId,
        string? userId,
        string fileName,
        StorageProvider storageProvider,
        MediaType mediaType)
    {
        // Create FileMetadata entity with all metadata
        var newFileMetadata = new FileMetadata(
            organizationId,
            fileName,
            bytes.Length,
            storageProvider);

        var newDocument = new Document(newFileMetadata, mediaType)
        {
            UserId = userId,
        };

        await AddAsync(newDocument);

        return newDocument;
    }

    private async Task<FileMetadata?> GetFileMetadataByNameAndSizeAsync(string name, long size, string organizationId)
    {
        return await Context
            .Set<FileMetadata>()
            .FirstOrDefaultAsync(f => !f.IsDeleted && f.OrganizationId == organizationId && f.Name == name && f.SizeBytes == size);
    }

    public async Task<HashSet<string>> GetIdsReadByUnfinishedReportsAsync(
        IReadOnlyCollection<string> documentIds,
        string organizationId,
        CancellationToken cancellationToken)
    {
        IQueryable<string?> unfinishedJobIds = Context.Set<WorkflowExecution>()
            .Where(WorkflowExecutionExpressions.ByOrganization(organizationId))
            .Where(WorkflowExecutionExpressions.IsInActiveState)
            .Select(execution => execution.JobId);

        IQueryable<DocumentInfo> unfinishedInput = Context.Set<DocumentInfo>().Where(DocumentInfo.IsRunInputOfAny(unfinishedJobIds));

        return await unfinishedInput
            .Where(info => documentIds.Contains(info.DocumentId))
            .Select(info => info.DocumentId)
            .Union(unfinishedInput
                .Where(info => info.Document!.MadeFromDocumentId != null && documentIds.Contains(info.Document.MadeFromDocumentId!))
                .Select(info => info.Document!.MadeFromDocumentId!))
            .ToHashSetAsync(cancellationToken);
    }

    public async Task<List<string>> GetDeletedRunInputFileNamesAsync(string jobId, CancellationToken cancellationToken)
    {
        return await Context.Set<DocumentInfo>()
            .IgnoreQueryFilters()
            .Where(DocumentInfo.IsRunInputOf(jobId))
            .Where(info => info.Document != null && info.Document.IsDeleted)
            .OrderBy(info => info.CreatedAt)
            .Select(info => Document.FileNameOf(info.Document!.Name, info.Document.FileMetadata == null ? null : info.Document.FileMetadata.Extension))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<DocumentInfo>> GetRunInputByJobIdAsync(string jobId)
    {
        return await Context.Set<DocumentInfo>()
            .Where(DocumentInfo.IsRunInputOf(jobId))
            .ToListAsync();
    }

    public async Task<List<DocumentInfo>> GetByJobIdWithMetadataAsync(string jobId)
    {
        return await ByJobIdWithMetadata(Context.Set<DocumentInfo>(), jobId).ToListAsync();
    }

    public async Task<List<DocumentInfo>> GetByJobIdWithDeletedDocumentsAsync(string jobId)
    {
        return await ByJobIdWithMetadata(Context.Set<DocumentInfo>().IgnoreQueryFilters().AsNoTracking(), jobId).ToListAsync();
    }

    private static IQueryable<DocumentInfo> ByJobIdWithMetadata(IQueryable<DocumentInfo> documentInfos, string jobId)
    {
        return documentInfos
            .Where(di => di.JobId == jobId)
            .Include(di => di.Document)
                .ThenInclude(d => d!.FileMetadata);
    }

    public void RemovePagesRange(IEnumerable<Page> pages)
    {
        Context.Set<Page>().RemoveRange(pages);
    }

    public void RemoveEmbeddingVectorsRange(IEnumerable<EmbeddingVector> vectors)
    {
        Context.Set<EmbeddingVector>().RemoveRange(vectors);
    }

    /// <inheritdoc />
    public async Task<List<Document>> GetDocumentsWithInfoAsync(
        List<string> fileIds,
        string organizationId,
        IEnumerable<(string FileId, string DocumentClass, bool ShouldUseOcr, bool ShouldBeDisplayedInOutput)> fileInfos)
    {
        if (fileIds.Count == 0)
        {
            return [];
        }

        // Fetch documents by IDs within organization (tenant isolation)
        List<Document> documents = await Entities
            .Include(d => d.Pages)
                .ThenInclude(p => p.EmbeddingVector)
            .Include(d => d.FileMetadata)
            .Where(d => fileIds.Contains(d.Id) && d.OrganizationId == organizationId && !d.IsDeleted)
            .ToListAsync();

        // Create a lookup for file infos by FileId, handling duplicates defensively
        var fileInfoLookup = fileInfos
            .GroupBy(f => f.FileId)
            .ToDictionary(g => g.Key, g => g.First());

        // Attach DocumentInfo metadata to each document
        foreach (Document? document in documents)
        {
            if (fileInfoLookup.TryGetValue(document.Id, out (string FileId, string DocumentClass, bool ShouldUseOcr, bool ShouldBeDisplayedInOutput) fileInfo))
            {
                document.CurrentDocumentInfo = new DocumentInfo(document.Id)
                {
                    Document = document,
                    DocumentClass = fileInfo.DocumentClass,
                    ShouldUseOcr = fileInfo.ShouldUseOcr,
                    ShouldBeDisplayedInOutput = fileInfo.ShouldBeDisplayedInOutput,
                };
            }
        }

        return documents;
    }
}
