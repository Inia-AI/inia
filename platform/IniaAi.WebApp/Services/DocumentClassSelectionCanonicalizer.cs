using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Keeps Any, None and the concrete document classes mutually exclusive in a multi-select.
/// </summary>
public static class DocumentClassSelectionCanonicalizer
{
    /// <summary>
    /// The selection after one click: a newly ticked Any or None replaces the whole selection, a newly
    /// ticked class unticks both sentinels, and an untick stands as it is.
    /// </summary>
    public static HashSet<string> ApplyClick(IReadOnlyCollection<string> previous, IReadOnlyCollection<string> incoming)
    {
        List<string> added = [.. incoming.Except(previous)];

        if (added.Contains(DocumentClass.KEY_ANY))
        {
            return [DocumentClass.KEY_ANY];
        }

        if (added.Contains(DocumentClass.KEY_NONE))
        {
            return [DocumentClass.KEY_NONE];
        }

        if (added.Count > 0)
        {
            return [.. incoming.Where(static item => !VariantDocumentClassEditor.IsSentinel(item))];
        }

        return [.. incoming];
    }

    /// <summary>
    /// The selection the block saves when the popover closes: emptying a previously non-empty selection
    /// means None, and an untouched empty selection stays unset.
    /// </summary>
    public static IReadOnlyList<string> Commit(IReadOnlyCollection<string> opened, IReadOnlyList<string> closing)
    {
        if (closing.Count == 0 && opened.Count > 0)
        {
            return [DocumentClass.KEY_NONE];
        }

        return closing;
    }
}
