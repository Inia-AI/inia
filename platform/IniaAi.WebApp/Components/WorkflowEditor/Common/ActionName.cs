namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// The accessible name of an action on a section or block card: its visible label, then the card
/// it acts on, so a screen-reader user who lists a page's buttons can tell which card each one targets.
/// </summary>
public static class ActionName
{
    public static string For(string action, string subject)
    {
        return $"{action}: {subject}";
    }
}
