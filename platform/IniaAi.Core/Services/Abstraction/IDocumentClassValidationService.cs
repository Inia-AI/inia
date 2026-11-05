using IniaAi.Core.Services.DocumentClassValidation;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Core.Services.Abstraction;

public interface IDocumentClassValidationService
{
    /// <summary>
    /// Validates that the uploaded documents satisfy the workflow's document class requirements.
    /// </summary>
    /// <param name="workflow">The workflow with document class definitions</param>
    /// <param name="documents">The list of uploaded documents</param>
    /// <returns>Validation result with errors, warnings, and status for each document class</returns>
    DocumentClassValidationResult ValidateDocumentClasses(Workflow workflow, IEnumerable<Document> documents);

    /// <summary>
    /// Validates that documents sent under these classes, one key per document (null or blank for
    /// a document with no class), satisfy the workflow's document class requirements.
    /// </summary>
    DocumentClassValidationResult ValidateDocumentClasses(Workflow workflow, IEnumerable<string?> documentClasses);
}
