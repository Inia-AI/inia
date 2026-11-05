using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Persistence.UserData.Repositories;

/// <summary>
/// Represents a repository for managing documents.
/// </summary>
public interface IDocumentRepository : IRepository<Document>
{
    Task<Document?> GetDocumentByFileMetadataIdAsync(string id);
    Task<Document?> GetDocumentByFileNameAndSize(string fileName, int sizeBytes, string organizationId);
    Task<IEnumerable<Document>> FindByCounterpartyIdAsync(string counterpartyId);
    /// <summary>The organization's live document of this name and size, which an upload of the same file reuses instead of storing it again.</summary>
    Task<Document?> FindExistingDocumentAsync(string fileName, long fileSize, string organizationId);
    Task<(Document Document, bool Created)> GetOrCreateDocumentAsync(
        byte[] bytes,
        string organizationId,
        string? userId,
        string fileName,
        long fileSize,
        Enums.StorageProvider storageProvider,
        MediaType mediaType);
    Task<Document> UploadNewFileCreateNewDocumentAsync(
        byte[] bytes,
        string organizationId,
        string? userId,
        string fileName,
        Enums.StorageProvider storageProvider,
        MediaType mediaType);
    Task<DocumentInfo> CreateDocumentInfoAsync(Document document);
    Task<IEnumerable<DocumentInfo>> GetDocumentInfosByJobIdOrWorkTypeIdAsync(string jobId, string organizationId, string? workflowTypeId = null);
    Task AddDocumentInfoAsync(DocumentInfo documentInfo);
    void RemoveDocumentInfo(DocumentInfo documentInfo);
    /// <summary>The job's run input: a document taken out of the run is left out.</summary>
    Task<List<DocumentInfo>> GetRunInputByJobIdAsync(string jobId);

    /// <summary>Every document the job was given, including one taken out of the run, with its file metadata.</summary>
    Task<List<DocumentInfo>> GetByJobIdWithMetadataAsync(string jobId);
    void RemovePagesRange(IEnumerable<Page> pages);
    void RemoveEmbeddingVectorsRange(IEnumerable<EmbeddingVector> vectors);

    /// <summary>
    /// Retrieves documents by their IDs within a specific organization and attaches DocumentInfo metadata.
    /// </summary>
    /// <param name="fileIds">The list of file/document IDs to retrieve.</param>
    /// <param name="organizationId">The organization ID for tenant isolation.</param>
    /// <param name="fileInfos">The file metadata requests containing class, tags, and OCR settings.</param>
    /// <returns>A list of documents with CurrentDocumentInfo populated.</returns>
    Task<List<Document>> GetDocumentsWithInfoAsync(
        List<string> fileIds,
        string organizationId,
        IEnumerable<(string FileId, string DocumentClass, bool ShouldUseOcr, bool ShouldBeDisplayedInOutput)> fileInfos);
}
