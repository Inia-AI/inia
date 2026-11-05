using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApp.Components.WorkflowEditor.Dialogs;

public record NewSharedComponentSpec(SharedComponentKind Kind, string Name, string CategoryId, string? Description);
