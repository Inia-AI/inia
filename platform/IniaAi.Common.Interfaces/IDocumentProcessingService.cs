using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;

namespace IniaAi.Common.Interfaces;

/// <summary>
/// Service responsible for processing documents (extracting pages, generating embeddings)
/// and loading context into workflows.
/// </summary>
public interface IDocumentProcessingService
{
    /// <summary>
    /// Processes documents by extracting pages and generating embeddings.
    /// This is required before documents can be used with RAG.
    /// </summary>
    /// <param name="documentInfos">The list of document infos to process.</param>
    /// <param name="userDataUnitOfWork">The unit of work for database operations.</param>
    /// <param name="workflow">The workflow the documents belong to, whose document classes and overview prompts they take. Null for documents processed outside a workflow, which get no new overviews and keep the ones an earlier run wrote.</param>
    /// <param name="workflowTypeId">Optional workflow type ID.</param>
    /// <param name="workflowExecutionId">Optional workflow execution ID for token usage tracking.</param>
    /// <param name="userId">Optional user ID for token usage tracking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessDocumentsAsync(
        List<DocumentInfo> documentInfos,
        IUserDataUnitOfWork userDataUnitOfWork,
        Workflow? workflow,
        string? workflowTypeId = null,
        string? workflowExecutionId = null,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads context from attached files into the workflow handler's workflow.
    /// </summary>
    /// <param name="documentInfos">The collection of documents to load context from.</param>
    /// <param name="workflow">The workflow to load context into.</param>
    /// <param name="allPages">The list to append pages to.</param>
    /// <param name="onDocumentProcessed">Optional callback when a document is processed, given the document's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LoadContextToWorkflowAsync(
        ICollection<DocumentInfo> documentInfos,
        Workflow workflow,
        List<Page> allPages,
        Func<string?, Task>? onDocumentProcessed = null,
        CancellationToken cancellationToken = default);
}
