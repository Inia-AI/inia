using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates.SaveWorkflowTemplateContent;

/// <summary>
/// Saves the full content of a workflow template from a raw JSON payload: structurally
/// validates the JSON, upserts the workflow type when it does not exist, and delegates the
/// actual save to <see cref="Commands.Workflows.SaveWorkflow.SaveWorkflowCommand"/> with
/// optimistic concurrency enforced.
/// </summary>
/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: templates are authored
/// deployment-wide, and the EDITOR_ROLES gate on the API mouth is the authoring boundary.
/// OrganizationId stays in the contract but does not filter which types can be saved; it is
/// only used when creating a new type, where it seeds the access row that makes the type
/// consumable by the creating organization.
/// </remarks>
/// <param name="WorkflowJson">The full workflow template JSON (verbatim PascalCase domain representation).</param>
/// <param name="BaseVersionId">The head version the caller based its edit on; null only for a first save.</param>
/// <param name="NewTypeName">Display name when creating the workflow type; defaults to the type id.</param>
/// <param name="NewTypeDescription">Optional description when creating the workflow type.</param>
/// <param name="NewTypeCategoryId">Category when creating the workflow type; defaults to the monitoring category.</param>
/// <param name="ConfirmSignatureFieldDeletions">Explicit acknowledgement that this save may delete signature fields holding client override/pending rows; without it such a save is rejected with <c>Signatures.DeletionRequiresConfirmation</c>.</param>
public record SaveWorkflowTemplateContentCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string WorkflowJson,
    string? BaseVersionId,
    string? NewTypeName = null,
    string? NewTypeDescription = null,
    string? NewTypeCategoryId = null,
    bool ConfirmSignatureFieldDeletions = false) : IRequest<ErrorOr<SaveWorkflowTemplateContentResult>>;

/// <summary>
/// Result of saving workflow template content.
/// </summary>
/// <param name="VersionId">The head version after the save — a new snapshot, or the prior head when deduped.</param>
/// <param name="ContentHash">The canonical content hash of the saved template.</param>
/// <param name="WasDeduped">True when the save matched the prior head and appended no new version.</param>
/// <param name="CreatedWorkflowType">True when the save created the workflow type (upsert path).</param>
/// <param name="Warnings">Non-blocking structural issues found in the saved template.</param>
/// <param name="SignatureSync">Signature-override bookkeeping (renames carried, rows deleted) so API callers see when client data was touched.</param>
public record SaveWorkflowTemplateContentResult(
    string VersionId,
    string ContentHash,
    bool WasDeduped,
    bool CreatedWorkflowType,
    IReadOnlyList<WorkflowStructuralValidationWarning> Warnings,
    Common.Signatures.SignatureSyncResult SignatureSync);
