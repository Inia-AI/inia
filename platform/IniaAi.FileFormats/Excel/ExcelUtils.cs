using IniaAi.Common;
using IniaAi.Common.Text;
using System.Text;

namespace IniaAi.FileFormats.Excel;

public static class ExcelUtils
{
    private const int MAX_COLUMNS = 400;

    // Renders a sheet's used range as a clean markdown table — no row-number column,
    // no cell-coordinate header, no row cap. Treats the first row as the table header
    // so Markdig-style renderers produce a proper table. Used by pipelines that need
    // the full sheet content (e.g. block-generated analysis output), not the capped
    // sample used by RAG previews.
    public static string GetSheetMarkdown(IWorkbookSheet sheet)
    {
        (int firstRow, int firstCol, int totalRows, int totalCols)? dataRange = GetActualDataRange(sheet);
        if (dataRange is null)
        {
            return string.Empty;
        }

        int firstRow = dataRange.Value.firstRow;
        int firstCol = dataRange.Value.firstCol;
        int totalRows = dataRange.Value.totalRows;
        int totalCols = dataRange.Value.totalCols;

        var headerRange = new ExcelRange(sheet, firstRow, firstCol, 1, totalCols);
        string headerMarkdown = ExcelRange.GetMarkdownFromRange(headerRange, isHeader: true);

        var dataSubrange = new ExcelRange(sheet, firstRow + 1, firstCol, totalRows - 1, totalCols);
        string dataMarkdown = ExcelRange.GetMarkdownFromRange(dataSubrange, isHeader: false);

        string combined = totalRows > 1 ? $"{headerMarkdown}\n{dataMarkdown}" : headerMarkdown;
        return MarkdownFormatter.FormatMarkdownTable(combined, alignColumns: false);
    }

    public static string GetSheetSampleMarkdown(IWorkbookSheet sheet, int maxRow = 50)
    {
        (int firstRow, int firstCol, int totalRows, int totalCols)? dataRange = GetActualDataRange(sheet);

        if (dataRange is null)
        {
            return string.Empty;
        }

        int firstRow = dataRange.Value.firstRow;
        int firstCol = dataRange.Value.firstCol;
        int totalRows = Math.Min(dataRange.Value.totalRows, maxRow);
        int totalCols = dataRange.Value.totalCols;

        var sb = new StringBuilder();

        _ = sb.Append("| |");
        for (int i = 0; i < totalCols; i++)
        {
            _ = sb.Append($"{Utils.ConvertIntToExcelColumnLetters(firstCol + i + 1)}|");
        }

        _ = sb.AppendLine();

        _ = sb.Append("|---|");
        for (int i = 0; i < totalCols; i++)
        {
            _ = sb.Append("---|");
        }

        _ = sb.AppendLine();

        for (int row = 0; row < totalRows; row++)
        {
            _ = sb.Append($"|{firstRow + row + 1}|");
            for (int col = 0; col < totalCols; col++)
            {
                string cellValue = sheet.CellText(firstRow + row, firstCol + col);
                _ = sb.Append(string.IsNullOrWhiteSpace(cellValue) ? " |" : $"{cellValue}|");
            }

            _ = sb.AppendLine();
        }

        return MarkdownFormatter.FormatMarkdownTable(sb.ToString(), alignColumns: false);
    }

    public static int GetMaxColumn(IWorkbookSheet sheet)
    {
        return Math.Min(sheet.MaxDataColumn + 1, MAX_COLUMNS);
    }

    /// <summary>
    /// Finds the bounding box of cells that contain actual visible content,
    /// ignoring cells that only have formatting (borders, fills, etc.) but no text.
    /// </summary>
    /// <returns>
    /// A tuple of (firstRow, firstCol, totalRows, totalCols) representing the
    /// 0-based bounding box of non-empty cells, or null if the sheet has no data.
    /// </returns>
    public static (int firstRow, int firstCol, int totalRows, int totalCols)? GetActualDataRange(IWorkbookSheet sheet)
    {
        int minRow = int.MaxValue, maxRow = -1, minCol = int.MaxValue, maxCol = -1;
        int lastRow = sheet.MaxDataRow;
        int lastCol = GetMaxColumn(sheet) - 1;

        for (int r = 0; r <= lastRow; r++)
        {
            for (int c = 0; c <= lastCol; c++)
            {
                if (string.IsNullOrWhiteSpace(sheet.CellText(r, c)))
                {
                    continue;
                }

                minRow = Math.Min(minRow, r);
                maxRow = Math.Max(maxRow, r);
                minCol = Math.Min(minCol, c);
                maxCol = Math.Max(maxCol, c);
            }
        }

        return maxRow == -1 || maxCol == -1
            ? null
            : (minRow, minCol, maxRow - minRow + 1, maxCol - minCol + 1);
    }
}
