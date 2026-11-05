namespace IniaAi.WebApi.Contracts.Requests.WorkflowTemplates;

/// <summary>
/// Request to duplicate a workflow template into a new workflow type, keeping every
/// shared-component link live and every per-entry deviation intact.
/// </summary>
/// <param name="NewWorkflowTypeId">The identifier of the workflow type to create; must not already exist.</param>
/// <param name="Name">The display name of the new workflow type.</param>
/// <param name="Description">The optional description; defaults to the source type's description.</param>
/// <param name="CategoryId">The category to assign; defaults to the source type's category.</param>
public record DuplicateWorkflowTypeRequest(
    string NewWorkflowTypeId,
    string Name,
    string? Description = null,
    string? CategoryId = null);
