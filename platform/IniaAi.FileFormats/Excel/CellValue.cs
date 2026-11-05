namespace IniaAi.FileFormats.Excel;

/// <summary>A cell's value: the one the file stores, or the one its formula last calculated.</summary>
public abstract record CellValue
{
    private CellValue()
    {
    }

    public static CellValue Empty { get; } = new Blank();

    public sealed record Blank : CellValue;

    public sealed record Number(double Value) : CellValue;

    /// <summary>A number whose number format shows a date or a time.</summary>
    public sealed record Date(DateTime Value) : CellValue;

    public sealed record Text(string Value) : CellValue;

    public sealed record Boolean(bool Value) : CellValue;

    /// <summary>An Excel error value, such as #DIV/0! or #NAME?.</summary>
    public sealed record Error(string Code) : CellValue;
}
