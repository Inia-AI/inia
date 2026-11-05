namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// A designer page a closed module adds for its own workflow type, in place of the generic one.
/// </summary>
/// <param name="WorkflowTypeId">The workflow type the page edits.</param>
/// <param name="Roles">The comma-separated roles the page's gate admits.</param>
/// <param name="Icon">The icon of the type's tile on the Workflow Editors list.</param>
public sealed record ModuleDesignerPage(string WorkflowTypeId, string Roles, string Icon);
