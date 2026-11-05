namespace IniaAi.FileFormats.Excel;

/// <summary>A rectangle of cells, from its first row and column to its last, both included.</summary>
public readonly record struct CellRectangle(int FirstRow, int FirstColumn, int LastRow, int LastColumn)
{
    public static CellRectangle Single(int row, int column)
    {
        return new CellRectangle(row, column, row, column);
    }
}

/// <param name="Area">The merged cells the cell belongs to, or the cell alone.</param>
/// <param name="WrapsText">Whether the cell wraps its text onto more lines.</param>
/// <param name="FontSize">The size of the cell's font, in points.</param>
public readonly record struct CellLayout(CellRectangle Area, bool WrapsText, double FontSize);

/// <param name="Text">The cell's text: what was typed, or what its formula last calculated.</param>
/// <param name="IsFormula">Whether a formula produced the text.</param>
public readonly record struct TextCell(int Row, int Column, string Text, bool IsFormula);

/// <summary>A run of characters in a cell's text, counted in UTF-16 code units.</summary>
public readonly record struct TextRun(int Start, int Length);

/// <summary>A cell of a named sheet.</summary>
public readonly record struct SheetCell(string Sheet, int Row, int Column)
{
    public override string ToString()
    {
        return $"{Sheet}!{CellAddress.Name(Row, Column)}";
    }
}

public enum SheetVisibility
{
    Visible,
    Hidden,
    VeryHidden,
}

public static class CellAddress
{
    /// <summary>The cell's name in A1 notation, such as "B7" for row 6 and column 1.</summary>
    public static string Name(int row, int column)
    {
        return $"{Common.Utils.ConvertIntToExcelColumnLetters(column + 1)}{row + 1}";
    }
}
