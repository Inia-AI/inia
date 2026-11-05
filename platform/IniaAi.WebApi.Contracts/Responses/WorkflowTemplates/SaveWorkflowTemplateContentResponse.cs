namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// Result of saving workflow template content.
/// </summary>
/// <param name="VersionId">The head version after the save — a new snapshot, or the prior head when deduped.</param>
/// <param name="ContentHash">The canonical content hash of the saved template.</param>
/// <param name="WasDeduped">True when the save matched the prior head and appended no new version.</param>
/// <param name="CreatedWorkflowType">True when the save created the workflow type (upsert path).</param>
/// <param name="Warnings">Non-blocking structural issues found in the saved template.</param>
public record SaveWorkflowTemplateContentResponse(
    string VersionId,
    string ContentHash,
    bool WasDeduped,
    bool CreatedWorkflowType,
    List<WorkflowTemplateWarningDto> Warnings);

/// <summary>
/// A non-blocking structural issue found in a workflow template.
/// </summary>
/// <param name="Path">JSON-path-style location of the issue (e.g. <c>Sections[2].Blocks[1]</c>).</param>
/// <param name="Message">Human-readable description of the issue.</param>
public record WorkflowTemplateWarningDto(string Path, string Message);
