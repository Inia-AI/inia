namespace IniaAi.FileFormats.Excel;

/// <summary>A workbook open for writing. Nothing reaches a file until <see cref="Save"/>.</summary>
public interface IEditableWorkbook : IDisposable
{
    /// <summary>The sheets in workbook order, hidden ones included.</summary>
    IReadOnlyList<IEditableSheet> Sheets { get; }

    /// <summary>The sheet with this name, compared as Excel compares sheet names, ignoring case; null when there is none.</summary>
    IEditableSheet? FindSheet(string name);

    /// <summary>
    /// Calculates every formula and stores each result in its cell, so a reader that does not calculate sees values.
    /// A formula the engine cannot evaluate gets an Excel error value, as Excel gives it, or is listed in
    /// <see cref="FormulaCalculation.Uncalculated"/>: it never keeps the value the file held without a word.
    /// The calculation stops at <paramref name="timeLimit"/>.
    /// </summary>
    FormulaCalculation CalculateFormulas(TimeSpan timeLimit);

    /// <summary>
    /// Starts measuring how text wraps, for fitting rows to their text. Dispose the measurement to end it; the
    /// workbook is as it was before.
    /// </summary>
    IWrappedTextMeasurement MeasureWrappedText();

    /// <summary>
    /// Removes each workbook-level defined name whose reference is empty or contains #REF!, which can make Excel
    /// report the file as damaged. A name that belongs to one sheet stays.
    /// </summary>
    void RemoveBrokenNames();

    /// <summary>
    /// Adds an empty sheet with this name and visibility after the last sheet. A sheet that already has the name
    /// is removed first.
    /// </summary>
    IEditableSheet ReplaceSheet(string name, SheetVisibility visibility);

    /// <summary>
    /// The workbook as a file in its own format: a macro-enabled workbook stays .xlsm with its macros, and any other
    /// workbook becomes .xlsx.
    /// </summary>
    byte[] Save();
}
