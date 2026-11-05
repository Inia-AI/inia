namespace IniaAi.FileFormats.Excel;

/// <summary>One sheet of a workbook. Rows, columns and the sheet's <see cref="Index"/> start at 0.</summary>
public interface IWorkbookSheet
{
    string Name { get; }

    int Index { get; }

    bool IsVisible { get; }

    /// <summary>The last row that holds a value, or -1 for an empty sheet.</summary>
    int MaxDataRow { get; }

    /// <summary>The last column that holds a value, or -1 for an empty sheet.</summary>
    int MaxDataColumn { get; }

    /// <summary>
    /// The cell as Excel shows it: text as typed, an error as its code (such as #DIV/0!), a boolean as
    /// TRUE or FALSE, a number or date in its number format with runs of spaces collapsed and trimmed,
    /// and an empty string for no value.
    /// </summary>
    string CellText(int row, int column);
}
