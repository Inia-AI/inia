namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// Lightweight summary of a workflow template type for list views.
/// </summary>
/// <param name="Id">The workflow type identifier.</param>
/// <param name="Name">The display name of the workflow type.</param>
/// <param name="Description">The optional description of the workflow type.</param>
/// <param name="CategoryId">The identifier of the category the workflow type belongs to.</param>
/// <param name="CategoryName">The display name of the category, when one is assigned.</param>
/// <param name="HasContent">True when a current workflow template exists for this type.</param>
public record WorkflowTemplateSummaryDto(
    string Id,
    string Name,
    string? Description,
    string? CategoryId,
    string? CategoryName,
    bool HasContent);
