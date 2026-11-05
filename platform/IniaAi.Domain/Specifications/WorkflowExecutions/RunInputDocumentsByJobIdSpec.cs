using Ardalis.Specification;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Domain.Specifications.WorkflowExecutions;

/// <summary>
/// A job's run input documents, each with its class, its file name and whether the document can
/// still be sent: it exists in the organization and was not deleted. Query filters are ignored so
/// a deleted document still reports its name.
/// </summary>
public sealed class RunInputDocumentsByJobIdSpec : Specification<DocumentInfo, RunInputDocument>
{
    public RunInputDocumentsByJobIdSpec(string jobId, string organizationId)
    {
        Query
            .Where(DocumentInfo.IsRunInputOf(jobId))
            .AsNoTracking()
            .IgnoreQueryFilters()
            .OrderBy(documentInfo => documentInfo.CreatedAt)
            .Select(documentInfo => new RunInputDocument(
                documentInfo.DocumentId,
                documentInfo.DocumentClass,
                documentInfo.Document == null || documentInfo.Document.OrganizationId != organizationId
                    ? null
                    : Document.FileNameOf(
                        documentInfo.Document.Name,
                        documentInfo.Document.FileMetadata == null ? null : documentInfo.Document.FileMetadata.Extension),
                documentInfo.Document != null
                    && documentInfo.Document.OrganizationId == organizationId
                    && !documentInfo.Document.IsDeleted));
    }
}

/// <summary>
/// One run input document of a job.
/// </summary>
/// <param name="DocumentId">The document's id.</param>
/// <param name="DocumentClass">The class the run sent it as; empty when it had none.</param>
/// <param name="FileName">The document's file name; null when the document row is gone.</param>
/// <param name="IsAvailable">Whether a new run can still be sent the document.</param>
public sealed record RunInputDocument(string DocumentId, string DocumentClass, string? FileName, bool IsAvailable);
