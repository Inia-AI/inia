namespace IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;

/// <summary>
/// What a save would do with this payload, reported without writing anything.
/// </summary>
/// <param name="WouldCreateWorkflowType">
/// True when no workflow type carries this id yet, so the save would create one. It is the signal
/// that matters on a save with a null base version, where the upsert turns a mistyped id into a
/// second workflow type; a save that carries a base version already conflicts on its own.
/// </param>
/// <param name="HeadVersionId">The current head version id, or null when the template has none yet.</param>
/// <param name="Warnings">Non-blocking structural issues the save would report alongside a success.</param>
public record ValidateWorkflowTemplateContentResponse(
    bool WouldCreateWorkflowType,
    string? HeadVersionId,
    List<WorkflowTemplateWarningDto> Warnings);
