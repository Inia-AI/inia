using System.Text.Json;

namespace IniaAi.WebApi.Contracts.Requests.WorkflowTemplates;

/// <summary>
/// Request to save the full content of a workflow template.
/// </summary>
/// <param name="Workflow">
/// The full workflow template JSON in the verbatim PascalCase domain representation,
/// as returned by the content GET endpoint. The payload is passed through unmodified.
/// </param>
/// <param name="BaseVersionId">
/// The head version identifier the edit was based on. Required whenever a head version
/// exists; a stale or missing value is rejected with 409 Conflict. Omit (null) only for
/// the first save of a workflow type that has no version history yet.
/// </param>
/// <param name="NewWorkflowType">
/// Metadata for creating the workflow type when it does not exist yet (upsert path).
/// Ignored when the workflow type already exists.
/// </param>
/// <param name="ConfirmSignatureFieldDeletions">
/// Explicit acknowledgement that this save may delete signature fields holding client
/// override/pending rows across organizations. Without it such a save is rejected with
/// <c>Signatures.DeletionRequiresConfirmation</c> listing the affected fields and counts.
/// </param>
public record SaveWorkflowTemplateContentRequest(
    JsonElement Workflow,
    string? BaseVersionId,
    NewWorkflowTypeDto? NewWorkflowType = null,
    bool ConfirmSignatureFieldDeletions = false);

/// <summary>
/// Metadata for creating a new workflow type during an upsert save.
/// </summary>
/// <param name="Name">The display name of the new workflow type.</param>
/// <param name="Description">The optional description of the new workflow type.</param>
/// <param name="CategoryId">The category to assign; defaults to the monitoring category when omitted.</param>
public record NewWorkflowTypeDto(
    string Name,
    string? Description = null,
    string? CategoryId = null);
