using IniaAi.FileFormats.Excel;

namespace IniaAi.FileFormats.Word;

/// <summary>Which sheets of a workbook <see cref="IWorkbookToWordConverter"/> puts into the Word document.</summary>
public static class WordPageSheets
{
    /// <summary>The prefix of the sheets that go into the document.</summary>
    public const string PREFIX = "WordPage";

    private const string NO_VISIBLE_SHEET = "The workbook has no visible sheet to convert to Word.";

    /// <summary>
    /// The indices of every sheet whose name starts with <see cref="PREFIX"/>, ignoring case, in workbook order; with
    /// no such sheet, the index of the first visible sheet. It fails when the workbook has no visible sheet.
    /// </summary>
    public static int[] Select(IReadOnlyList<IWorkbookSheet> sheets)
    {
        int[] wordPages = [.. sheets
            .Where(sheet => sheet.Name.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase))
            .Select(sheet => sheet.Index)];
        if (wordPages.Length > 0)
        {
            return wordPages;
        }

        IWorkbookSheet firstVisible = sheets.FirstOrDefault(sheet => sheet.IsVisible)
            ?? throw new InvalidOperationException(NO_VISIBLE_SHEET);
        return [firstVisible.Index];
    }
}
