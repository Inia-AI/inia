using IniaAi.FileFormats.Excel;
using System.Collections;

namespace IniaAi.FileFormats.Pages;

/// <summary>
/// One sheet's size as processing pages it: each cell with text adds its length and a separator, each row with text
/// adds two more, and from the first row with text, every page of <see cref="ExcelUtils.ROWS_PER_PAGE"/> rows that
/// holds text counts. A cell past the last row a sheet can have counts nothing.
/// </summary>
internal sealed class SheetMeasure
{
    private const int MAX_ROWS = 1_048_576;

    private readonly BitArray _rowsWithText = new(0);
    private int _firstRow = int.MaxValue;
    private int _lastRow = -1;

    public long Characters { get; private set; }

    public void Add(int row, int length)
    {
        if (length == 0 || row < 0 || row >= MAX_ROWS)
        {
            return;
        }

        Characters += length + 1;
        if (row >= _rowsWithText.Length)
        {
            _rowsWithText.Length = Math.Min(MAX_ROWS, Math.Max(row + 1, _rowsWithText.Length * 2));
        }

        if (!_rowsWithText[row])
        {
            _rowsWithText[row] = true;
            Characters += 2;
        }

        _firstRow = Math.Min(_firstRow, row);
        _lastRow = Math.Max(_lastRow, row);
    }

    public int RowPages()
    {
        int rowPages = 0;
        int lastPage = -1;
        for (int row = _firstRow; row <= _lastRow; row++)
        {
            int page = (row - _firstRow) / ExcelUtils.ROWS_PER_PAGE;
            if (_rowsWithText[row] && page != lastPage)
            {
                rowPages++;
                lastPage = page;
            }
        }

        return rowPages;
    }
}
