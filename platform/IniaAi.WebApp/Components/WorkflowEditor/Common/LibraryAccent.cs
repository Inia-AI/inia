using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApp.Components.WorkflowEditor.State;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class LibraryAccent
{
    public const string BASE_CLASS = "editor-shared";
    public const string BROKEN_CLASS = "editor-shared--broken";
    public const string BADGE_CLASS = "library-accent-badge";

    public static string? BadgeAccent(LibraryLinkIndicator indicator)
    {
        return indicator is LibraryLinkIndicator.UpToDate or LibraryLinkIndicator.Retired
            ? BADGE_CLASS
            : null;
    }

    public static string CssClass(LibraryLinkIndicator indicator)
    {
        return indicator switch
        {
            LibraryLinkIndicator.UpToDate
                or LibraryLinkIndicator.UpdateAvailable
                or LibraryLinkIndicator.Retired => BASE_CLASS,
            LibraryLinkIndicator.Missing => $"{BASE_CLASS} {BROKEN_CLASS}",
            _ => string.Empty,
        };
    }

    public static string ClassSuffix(LibraryLinkIndex? linkIndex, Block block)
    {
        return Suffix(linkIndex?.For(block).Indicator);
    }

    public static string ClassSuffix(LibraryLinkIndex? linkIndex, Section section)
    {
        return Suffix(linkIndex?.For(section).Indicator);
    }

    private static string Suffix(LibraryLinkIndicator? indicator)
    {
        if (indicator is null)
        {
            return string.Empty;
        }

        string css = CssClass(indicator.Value);
        return css.Length == 0 ? string.Empty : $" {css}";
    }
}
