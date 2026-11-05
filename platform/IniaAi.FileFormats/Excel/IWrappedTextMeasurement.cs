namespace IniaAi.FileFormats.Excel;

/// <summary>A measurement of wrapped text, started by <see cref="IEditableWorkbook.MeasureWrappedText"/>.</summary>
public interface IWrappedTextMeasurement : IDisposable
{
    /// <summary>
    /// The number of lines the cell's text takes when it wraps across the cell's display area (the merged cells it
    /// starts, or the cell alone), in the cell's font and its bold runs; null when the implementation cannot lay
    /// out text. <paramref name="sheet"/> is a sheet of the workbook being measured.
    /// </summary>
    int? CountLines(IEditableSheet sheet, int row, int column);
}
