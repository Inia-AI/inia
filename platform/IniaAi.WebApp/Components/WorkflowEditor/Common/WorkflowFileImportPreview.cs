using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// The consent step before a workflow file replaces the open workflow: what the file holds, what
/// it replaces, and that nothing is saved yet. Either upload control reaches it.
/// </summary>
public static class WorkflowFileImportPreview
{
    private const string UNNAMED_WORKFLOW = "a workflow without a name";

    public static string TitleFor(bool fromBundleControl)
    {
        return fromBundleControl
            ? "That is a workflow file, not a bundle"
            : "Replace the editor content?";
    }

    public static string Describe(Workflow open, Workflow uploaded, string fileName)
    {
        string uploadedName = HasName(uploaded) ? $"the workflow '{uploaded.Name}'" : UNNAMED_WORKFLOW;
        string openName = HasName(open) ? $"'{open.Name}'" : UNNAMED_WORKFLOW;

        return $"The file '{fileName}' holds {uploadedName}: {Size(uploaded)}. "
            + $"Opening it replaces everything in this editor, now {openName}: {Size(open)}. "
            + "Nothing is saved until you press Save, so cancelling leaves this workflow untouched.";
    }

    private static bool HasName(Workflow workflow)
    {
        return !string.IsNullOrWhiteSpace(workflow.Name);
    }

    private static string Size(Workflow workflow)
    {
        int sections = workflow.Sections.Count;
        int blocks = workflow.Sections.Sum(section => section.Blocks.Count);

        return $"{sections} {(sections == 1 ? "section" : "sections")}, {blocks} {(blocks == 1 ? "block" : "blocks")}";
    }
}
