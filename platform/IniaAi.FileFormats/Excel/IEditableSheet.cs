namespace IniaAi.FileFormats.Excel;

/// <summary>
/// One sheet of an <see cref="IEditableWorkbook"/>. Rows and columns start at 0. A write replaces the cell's value
/// and keeps the cell's style.
/// </summary>
public interface IEditableSheet : IWorkbookSheet
{
    /// <summary>The cell's value: the one the file stores, or the one its formula last calculated.</summary>
    CellValue ReadValue(int row, int column);

    /// <summary>
    /// Every cell whose value is text, row by row and left to right: text typed into the cell, or text that a
    /// formula last calculated.
    /// </summary>
    IEnumerable<TextCell> TextCells();

    /// <summary>Writes text; the characters of each run in <paramref name="boldRuns"/> are bold, the rest keep the cell's font.</summary>
    void WriteText(int row, int column, string text, IReadOnlyList<TextRun> boldRuns);

    void WriteNumber(int row, int column, double value);

    void WriteBoolean(int row, int column, bool value);

    /// <summary>
    /// Writes the date as Excel stores one, a number of days. The cell keeps its number format, so it shows a date
    /// only when that format is a date format.
    /// </summary>
    void WriteDate(int row, int column, DateTime value);

    /// <summary>Removes the cell's value.</summary>
    void Clear(int row, int column);

    /// <summary>Whether the cell's number format shows a date or a time.</summary>
    bool HasDateFormat(int row, int column);

    /// <summary>Gives the cell an Excel number format, such as "yyyy-mm-dd", and keeps the rest of its style.</summary>
    void SetNumberFormat(int row, int column, string format);

    /// <summary>Where and how the cell shows its text.</summary>
    CellLayout Layout(int row, int column);

    bool IsRowHidden(int row);

    bool IsColumnHidden(int column);

    /// <summary>The row's height, in points.</summary>
    double RowHeight(int row);

    void SetRowHeight(int row, double points);
}
