using System.Text;

namespace IniaAi.FileFormats.Excel;

public class ExcelRange
{
    public IWorkbookSheet Sheet { get; }
    public int FirstRow { get; }
    public int FirstColumn { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }

    public ExcelRange(IWorkbookSheet sheet, int firstRow, int firstColumn, int rowCount, int columnCount)
    {
        Sheet = sheet;
        FirstRow = firstRow;
        FirstColumn = firstColumn;
        RowCount = rowCount;
        ColumnCount = columnCount;
    }

    public string this[int row, int col] => Sheet.CellText(FirstRow + row, FirstColumn + col);

    public static string GetMarkdownFromRange(ExcelRange range, bool isHeader)
    {
        var sb = new StringBuilder();

        for (int row = 0; row < range.RowCount; row++)
        {
            _ = sb.Append('|');

            for (int col = 0; col < range.ColumnCount; col++)
            {
                string cellValue = range[row, col];
                _ = sb.Append(string.IsNullOrWhiteSpace(cellValue) ? " " : cellValue);
                _ = sb.Append('|');
            }

            _ = sb.AppendLine();
        }

        if (isHeader)
        {
            _ = sb.Append('|');

            for (int col = 0; col < range.ColumnCount; col++)
            {
                _ = sb.Append("---|");
            }

            _ = sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    public static string CombineBlocksMarkdown(
        List<ExcelRange> blocks,
        int desiredRowCount,
        bool isHeader)
    {
        int totalCols = blocks.Sum(b => b.ColumnCount);

        var sb = new StringBuilder();

        for (int i = 0; i < desiredRowCount; i++)
        {
            _ = sb.Append('|');
            foreach (ExcelRange range in blocks)
            {
                for (int col = 0; col < range.ColumnCount; col++)
                {
                    string cellValue = "";
                    if (i < range.RowCount)
                    {
                        cellValue = range[i, col];
                    }

                    _ = sb.Append(string.IsNullOrWhiteSpace(cellValue) ? " " : cellValue);
                    _ = sb.Append('|');
                }
            }

            _ = sb.AppendLine();
        }

        if (isHeader)
        {
            _ = sb.Append('|');
            for (int i = 0; i < totalCols; i++)
            {
                _ = sb.Append("---|");
            }

            _ = sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    public bool IsEmpty(int firstDataRow, int firstDataCol)
    {
        for (int row = firstDataRow; row < FirstRow + RowCount; row++)
        {
            for (int col = firstDataCol; col < FirstColumn + ColumnCount; col++)
            {
                int relativeRow = row - FirstRow;
                int relativeCol = col - FirstColumn;

                if (!string.IsNullOrWhiteSpace(this[relativeRow, relativeCol]))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
